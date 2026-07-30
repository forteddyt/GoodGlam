using FluentAssertions;
using Xunit;

namespace GoodGlam.IntegrationTests.Harness;

/// <summary>
/// Harness tests for <see cref="RetryPolicy"/> and <see cref="LiveEc.RetryAsync{T}"/>.
///
/// These are deliberately *outside* the <see cref="LiveEc.Collection"/> collection: they exercise
/// the retry harness itself with an in-memory call, so they need no network and still pass while
/// Eorzea Collection is blocking us — which is exactly when the harness needs to be trustworthy.
/// </summary>
public sealed class RetryPolicyTests
{
    /// <summary>A fast policy so the behavioural tests below don't actually sleep for a minute.</summary>
    private static readonly RetryPolicy Fast = new(
        MaxAttempts: 4,
        BaseDelay: TimeSpan.FromMilliseconds(1),
        MaxDelay: TimeSpan.FromMilliseconds(2),
        Budget: TimeSpan.FromSeconds(30),
        PerAttemptTimeout: TimeSpan.FromSeconds(5));

    [Fact]
    public void Backoff_grows_exponentially_then_caps()
    {
        var policy = RetryPolicy.Default;

        // jitter = 1 is the top of the jitter band, i.e. the undamped exponential schedule.
        var delays = Enumerable.Range(1, policy.MaxAttempts - 1)
            .Select(attempt => policy.BackoffFor(attempt, jitter: 1).TotalSeconds)
            .ToArray();

        delays.Should().Equal(
            new[] { 2d, 4d, 8d, 16d, 20d, 20d, 20d },
            "the delay should double until it saturates at MaxDelay (20s)");
    }

    [Fact]
    public void Backoff_jitter_spans_the_documented_band()
    {
        var policy = RetryPolicy.Default;

        policy.BackoffFor(2, jitter: 1).Should().Be(TimeSpan.FromSeconds(4),
            "jitter = 1 yields the full exponential delay");
        policy.BackoffFor(2, jitter: 0).Should().Be(TimeSpan.FromSeconds(3),
            "jitter = 0 shaves off the 25% jitter band");
    }

    /// <summary>
    /// The whole point of this change: the old harness gave up after a fixed 7s, but the observed
    /// Cloudflare challenge on CI runners persists for tens of seconds. The retry schedule must
    /// keep trying across a ~minute-plus window.
    /// </summary>
    [Fact]
    public void Total_backoff_window_covers_a_minute_plus_outage()
    {
        var policy = RetryPolicy.Default;

        var shortest = policy.TotalBackoff(jitter: 0);
        var longest = policy.TotalBackoff(jitter: 1);

        shortest.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(60),
            "even the most-damped schedule has to outlast a challenge lasting about a minute");
        longest.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(90),
            "but the suite still has to fail in a reasonable time when EC is genuinely blocking us");
    }

    [Fact]
    public async Task Retries_until_the_call_returns_a_good_result()
    {
        var calls = 0;

        var result = await LiveEc.RetryAsync(
            _ =>
            {
                calls++;
                return Task.FromResult(calls < 3 ? null : "ok");
            },
            value => value is not null,
            Fast);

        result.Should().Be("ok");
        calls.Should().Be(3, "it should stop as soon as the result is acceptable");
    }

    [Fact]
    public async Task Returns_the_last_bad_result_after_exhausting_attempts()
    {
        var calls = 0;

        var result = await LiveEc.RetryAsync(
            _ =>
            {
                calls++;
                return Task.FromResult<string?>(null);
            },
            value => value is not null,
            Fast);

        result.Should().BeNull("the caller's assertion should produce the failure message");
        calls.Should().Be(Fast.MaxAttempts);
    }

    [Fact]
    public async Task Keeps_retrying_after_a_transient_exception()
    {
        var calls = 0;

        var result = await LiveEc.RetryAsync<string?>(
            _ =>
            {
                calls++;
                return calls < 3
                    ? throw new HttpRequestException("transient")
                    : Task.FromResult<string?>("ok");
            },
            value => value is not null,
            Fast);

        result.Should().Be("ok");
        calls.Should().Be(3);
    }

    /// <summary>
    /// A sustained failure must still surface its cause: the final attempt's exception propagates
    /// so <see cref="LiveEcFixture"/> can attach it as the inner exception.
    /// </summary>
    [Fact]
    public async Task Propagates_the_exception_from_the_final_attempt()
    {
        var act = async () => await LiveEc.RetryAsync<string?>(
            _ => throw new HttpRequestException("blocked"),
            value => value is not null,
            Fast);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("blocked");
    }

    [Fact]
    public async Task Stops_early_once_the_wall_clock_budget_is_exhausted()
    {
        var calls = 0;
        var policy = Fast with { MaxAttempts = 50, Budget = TimeSpan.FromMilliseconds(150) };

        var result = await LiveEc.RetryAsync(
            async ct =>
            {
                calls++;
                await Task.Delay(30, ct);
                return (string?)null;
            },
            value => value is not null,
            policy);

        result.Should().BeNull();
        calls.Should().BeLessThan(policy.MaxAttempts,
            "the budget, not the attempt count, should end a slow sustained outage");
    }

    /// <summary>
    /// The budget can end the run *before* the final attempt, so "rethrow only on the last attempt"
    /// is not enough on its own: without an explicit capture the cause is swallowed and the caller
    /// gets <c>default(T)</c>. That is the worst possible outcome here — for a non-nullable result
    /// like <c>GlamPopularity</c> the test would then die on a bare NullReferenceException with no
    /// mention of EC at all, hiding a genuine hang/timeout regression behind a null.
    /// </summary>
    [Fact]
    public async Task Surfaces_the_cause_when_the_budget_ends_a_run_of_failures()
    {
        var policy = Fast with { MaxAttempts = 50, Budget = TimeSpan.FromMilliseconds(150) };

        var act = async () => await LiveEc.RetryAsync<string?>(
            async ct =>
            {
                await Task.Delay(30, ct);
                throw new HttpRequestException("blocked");
            },
            value => value is not null,
            policy);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("blocked");
    }

    /// <summary>
    /// The same guarantee for the shape a hang actually takes: the per-attempt timeout, not the
    /// callee, is what throws. This is the case that matters on CI, where an attempt can burn its
    /// whole timeout and the budget then ends the run well short of MaxAttempts.
    /// </summary>
    [Fact]
    public async Task Surfaces_a_per_attempt_timeout_rather_than_returning_null()
    {
        var calls = 0;
        var policy = Fast with
        {
            MaxAttempts = 50,
            Budget = TimeSpan.FromMilliseconds(150),
            PerAttemptTimeout = TimeSpan.FromMilliseconds(20),
        };

        var act = async () => await LiveEc.RetryAsync<string?>(
            async ct =>
            {
                calls++;
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            },
            value => value is not null,
            policy);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().BeLessThan(policy.MaxAttempts, "the budget should have ended the run early");
    }

    /// <summary>
    /// The budget is a ceiling on the whole run, so it has to bound the attempt that is *running*,
    /// not just the sleep before it. Checking it only before sleeping let a final attempt start
    /// just under the ceiling and then run a full per-attempt timeout past it - with the default
    /// policy, ~172s against a documented 150s budget.
    /// </summary>
    [Fact]
    public async Task Bounds_a_running_attempt_by_the_remaining_budget()
    {
        var policy = new RetryPolicy(
            MaxAttempts: 8,
            BaseDelay: TimeSpan.FromMilliseconds(10),
            MaxDelay: TimeSpan.FromMilliseconds(10),
            Budget: TimeSpan.FromMilliseconds(300),
            PerAttemptTimeout: TimeSpan.FromSeconds(30));

        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        // Every attempt hangs until its token trips, so only the budget can end the run.
        var act = async () => await LiveEc.RetryAsync<string?>(
            async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            },
            value => value is not null,
            policy);

        await act.Should().ThrowAsync<OperationCanceledException>();

        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "the run must honour its budget rather than the far larger per-attempt timeout");
    }

    /// <summary>A later good result must clear an earlier failure rather than resurrect it at the end.</summary>
    [Fact]
    public async Task Does_not_resurface_an_earlier_exception_once_a_result_arrives()
    {
        var calls = 0;

        var result = await LiveEc.RetryAsync<string?>(
            _ =>
            {
                calls++;
                return calls == 1
                    ? throw new HttpRequestException("first attempt blew up")
                    : Task.FromResult<string?>(null);
            },
            value => value is not null,
            Fast);

        result.Should().BeNull(
            "the run ended on a bad-but-returned result, so the caller's own assertion should speak");
        calls.Should().Be(Fast.MaxAttempts);
    }
}
