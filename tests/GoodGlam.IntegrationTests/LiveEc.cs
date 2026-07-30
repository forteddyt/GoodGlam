using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Dalamud.Plugin.Services;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.IntegrationTests;

/// <summary>
/// A known Eorzea Collection gear piece: the game item ID the loot window would report, the
/// EC slot, and the EC item ID the bridge is expected to resolve it to. Used to assert the
/// real <c>game ID -&gt; EC ID</c> bridge against live EC.
/// </summary>
public sealed record EcFixture(string Name, GlamSlot Slot, uint GameItemId, int EcId);

/// <summary>Stable, reliably-popular fixtures verified against live EC.</summary>
public static class EcFixtures
{
    public static readonly EcFixture CavalryGauntlets = new("Cavalry Gauntlets", GlamSlot.Hands, 3610, 1870);
    public static readonly EcFixture ScionJacket = new("Scion Adventurer's Jacket", GlamSlot.Body, 17492, 8912);
    public static readonly EcFixture ScionBottoms = new("Scion Adventurer's Bottoms", GlamSlot.Legs, 17493, 8941);

    /// <summary>The piece used for the reachability probe — chosen for a large, stable loves count.</summary>
    public static readonly EcFixture Reachability = ScionJacket;

    /// <summary>Primitive rows for <c>[Theory]</c> data (xUnit-serializable): name, slot key, game ID, EC ID.</summary>
    public static IEnumerable<object[]> KnownItems =>
    [
        ["Cavalry Gauntlets", "hands", 3610u, 1870],
        ["Scion Adventurer's Jacket", "body", 17492u, 8912],
        ["Scion Adventurer's Bottoms", "legs", 17493u, 8941],
    ];
}

/// <summary>
/// The retry schedule the live-EC harness uses: capped exponential backoff with jitter, bounded by
/// both an attempt count and a wall-clock budget.
///
/// The shape is dictated by the failure it has to absorb. Eorzea Collection's Cloudflare edge
/// serves a managed bot challenge to GitHub-hosted runner IPs — an instant HTTP 403 (single-digit
/// to ~75ms, straight from the edge), applied to every transport alike. Observed on CI runners, it
/// can clear after a few seconds or persist for the better part of a minute. The previous schedule
/// (4 attempts, fixed 500·n² backoff) exhausted itself in 7s, i.e. right as EC would often start
/// letting requests through, which is what turned an environmental blip into a red build.
///
/// Jitter matters for the same reason: without it, every retry lands on the same cadence, which is
/// both needlessly bot-like and correlated across concurrent runs.
/// </summary>
internal sealed record RetryPolicy(
    int MaxAttempts,
    TimeSpan BaseDelay,
    TimeSpan MaxDelay,
    TimeSpan Budget,
    TimeSpan PerAttemptTimeout)
{
    /// <summary>Fraction of each delay that is randomised; the rest is the fixed exponential floor.</summary>
    private const double JitterFraction = 0.25;

    /// <summary>
    /// Backoff totalling ~67-90s across 8 attempts (2s doubling, capped at 20s), under a 150s
    /// wall-clock ceiling so a run where every attempt also times out still ends in bounded time.
    /// </summary>
    public static readonly RetryPolicy Default = new(
        MaxAttempts: 8,
        BaseDelay: TimeSpan.FromSeconds(2),
        MaxDelay: TimeSpan.FromSeconds(20),
        Budget: TimeSpan.FromSeconds(150),
        PerAttemptTimeout: TimeSpan.FromSeconds(30));

    /// <summary>
    /// The delay to wait after <paramref name="attempt"/> (1-based). <paramref name="jitter"/> is a
    /// 0-1 roll: 1 gives the full exponential delay, 0 the damped floor.
    /// </summary>
    public TimeSpan BackoffFor(int attempt, double jitter)
    {
        var exponential = this.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(exponential, this.MaxDelay.TotalMilliseconds);
        var scale = 1 - JitterFraction + (JitterFraction * Math.Clamp(jitter, 0, 1));
        return TimeSpan.FromMilliseconds(capped * scale);
    }

    /// <summary>Total time spent sleeping across a full run, for a constant jitter roll.</summary>
    public TimeSpan TotalBackoff(double jitter)
        => TimeSpan.FromMilliseconds(
            Enumerable.Range(1, this.MaxAttempts - 1)
                .Sum(attempt => this.BackoffFor(attempt, jitter).TotalMilliseconds));
}

/// <summary>
/// Shared helpers for the live Eorzea Collection tests: a no-op Dalamud log bootstrap and a
/// bounded-retry wrapper that smooths over transient network blips / rate limiting.
/// </summary>
public static class LiveEc
{
    /// <summary>All live-EC tests share this collection so they run serially (politeness + determinism).</summary>
    public const string Collection = "LiveEc";

    private static bool logInstalled;

    /// <summary>
    /// Installs a no-op <see cref="IPluginLog"/> into the plugin's static <c>Services.Log</c> so the
    /// EC client and transport (which log freely) don't dereference a null static under test.
    /// </summary>
    public static void EnsureLog()
    {
        if (logInstalled)
            return;

        var log = DispatchProxy.Create<IPluginLog, NoopLog>();
        typeof(GoodGlam.Services).GetProperty("Log", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, log);
        logInstalled = true;
    }

    /// <summary>
    /// Runs a live EC call with bounded exponential backoff + jitter, retrying until
    /// <paramref name="isGood"/> accepts the result or the <see cref="RetryPolicy"/> is exhausted,
    /// so only a sustained failure reaches the assertion. Each attempt is hard-bounded by
    /// <see cref="RetryPolicy.PerAttemptTimeout"/> and the run as a whole by
    /// <see cref="RetryPolicy.Budget"/>. The final (possibly "bad") result is returned so the
    /// caller's assertion produces the failure message.
    /// </summary>
    public static Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> call, Func<T, bool> isGood)
        => RetryAsync(call, isGood, RetryPolicy.Default);

    internal static async Task<T> RetryAsync<T>(
        Func<CancellationToken, Task<T>> call,
        Func<T, bool> isGood,
        RetryPolicy policy)
    {
        T result = default!;
        var hasResult = false;
        ExceptionDispatchInfo? lastFailure = null;
        var elapsed = Stopwatch.StartNew();

        for (var attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            // Bound the attempt by whatever is left of the budget as well as by its own timeout, so
            // the ceiling covers the attempt that is running rather than only the sleeps between
            // attempts. Without this a final attempt could start just under the budget and then run
            // a whole PerAttemptTimeout past it (~172s against a documented 150s ceiling).
            var remaining = policy.Budget - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;

            var budgetBound = remaining < policy.PerAttemptTimeout;
            using var cts = new CancellationTokenSource(budgetBound ? remaining : policy.PerAttemptTimeout);
            try
            {
                result = await call(cts.Token).WaitAsync(cts.Token).ConfigureAwait(false);

                // The attempt produced a result, so it — not any earlier throw — is what the caller
                // should see if this turns out to be the last one.
                hasResult = true;
                lastFailure = null;
                if (isGood(result))
                    return result;
            }
            catch (Exception ex)
            {
                // Being cut short by the budget says only that time ran out, which is never a better
                // explanation than a real earlier result or failure. Keep those instead.
                if (!budgetBound || (!hasResult && lastFailure is null))
                    lastFailure = ExceptionDispatchInfo.Capture(ex);
            }

            if (budgetBound)
                break;

            if (attempt == policy.MaxAttempts)
                break;

            // Stop once the next sleep would run past the budget: a run where every attempt also
            // burns its own timeout must still finish in bounded time.
            var backoff = policy.BackoffFor(attempt, Random.Shared.NextDouble());
            if (elapsed.Elapsed + backoff >= policy.Budget)
                break;

            await Task.Delay(backoff).ConfigureAwait(false);
        }

        // A run that only ever threw has to surface its cause. The budget can end the loop before
        // the final attempt — which is precisely what a timeout-shaped outage does — so rethrowing
        // only on the last attempt would drop the exception and hand back default(T). For a
        // non-nullable T that null goes on to fail the caller's assertion as a bare
        // NullReferenceException, hiding a real hang behind an unrelated error.
        lastFailure?.Throw();

        return result;
    }
}

internal class NoopLog : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
}

/// <summary>
/// Captures the most recent popularity notification so the end-to-end check-flow test can assert
/// the threshold path fired.
/// </summary>
internal sealed class CapturingNotifier : INotifier, INotificationTarget
{
    public int Count;
    public DropOccurrence? LastDrop;
    public GlamPopularity? LastPopularity;

    public INotificationTarget CaptureTarget() => this;

    public void NotifyPopular(DropOccurrence drop, GlamPopularity popularity)
    {
        this.Count++;
        this.LastDrop = drop;
        this.LastPopularity = popularity;
    }
}

/// <summary>
/// Collection fixture for the live-EC tests: bootstraps the log and verifies EC is actually
/// reachable before any test runs. A sustained outage (after retries) fails fast with a clear
/// message — these are blocking, highest-value end-to-end tests, so an unreachable EC is a hard
/// failure, not a skip.
/// </summary>
public sealed class LiveEcFixture
{
    private const string UnreachableMessage =
        "Eorzea Collection was unreachable after retries. The GoodGlam integration tests are " +
        "blocking, end-to-end tests that require live EC connectivity. Check network/Cloudflare " +
        "access and re-run (or run them manually if EC itself is down).";

    public LiveEcFixture()
    {
        LiveEc.EnsureLog();
        EnsureEcReachable();
    }

    private static void EnsureEcReachable()
    {
        var probe = EcFixtures.Reachability;
        var client = new EorzeaCollectionClient();

        EcItem? resolved;
        try
        {
            // Probe the real transport (managed HTTP under Wine/Linux, curl.exe on native Windows)
            // exactly the way the plugin reaches EC.
            resolved = LiveEc.RetryAsync(
                ct => client.ResolveEcItemAsync(probe.Slot, probe.Name, probe.GameItemId, ct),
                result => result is not null).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(Explain(probe), ex);
        }

        if (resolved is null)
            throw new InvalidOperationException(Explain(probe));
    }

    /// <summary>
    /// Builds the failure message. The plugin's transport reports every failure as <c>null</c>, so
    /// on the way out we re-probe once with a diagnostic request that keeps the status line and
    /// Cloudflare headers — turning "unreachable" into an actionable post-mortem (see
    /// <see cref="EcReachabilityReport"/>). Best effort: if the post-mortem itself fails we still
    /// report the original outage rather than masking it.
    /// </summary>
    private static string Explain(EcFixture probe)
    {
        string report;
        try
        {
            report = EcReachabilityProbe.RunAsync(probe).GetAwaiter().GetResult().Describe();
        }
        catch (Exception ex)
        {
            report = $"(diagnostic probe failed: {ex.GetType().Name}: {ex.Message})";
        }

        return $"{UnreachableMessage}{Environment.NewLine}{Environment.NewLine}{report}";
    }
}

[CollectionDefinition(LiveEc.Collection)]
public sealed class LiveEcCollection : ICollectionFixture<LiveEcFixture>
{
}
