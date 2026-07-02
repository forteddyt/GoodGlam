using System.Diagnostics;
using Dalamud.Interface.Textures.TextureWraps;
using FakeItEasy;
using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Exercises <see cref="GlamImageCache"/>: the lazy, load-once, per-URL texture cache behind the
/// History tab's hover preview. The texture loader is injected, so these run without a render device
/// or network — a fake loader hands back faked <see cref="IDalamudTextureWrap"/>s (or null/throws) to
/// drive the Ready/Failed states, and disposal is asserted against the fakes.
/// </summary>
public class GlamImageCacheTests
{
    public GlamImageCacheTests() => TestServices.EnsureLog();

    private const string Url = "https://glamours.ec/200/cover-0-9.png";

    /// <summary>Polls a condition briefly so the async load path can be asserted without flakiness.</summary>
    private static void WaitUntil(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < TimeSpan.FromSeconds(2))
            Thread.Sleep(5);
        condition().Should().BeTrue("the awaited condition should hold within the timeout");
    }

    [Fact]
    public void Loads_each_url_once_and_caches_the_wrap()
    {
        var wrap = A.Fake<IDalamudTextureWrap>();
        var calls = 0;
        using var cache = new GlamImageCache((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IDalamudTextureWrap?>(wrap);
        });

        var first = cache.Get(Url);
        var second = cache.Get(Url);
        var third = cache.Get(Url);

        calls.Should().Be(1, "a URL is fetched exactly once and then served from cache");
        first.State.Should().Be(GlamImageState.Ready);
        first.Texture.Should().BeSameAs(wrap);
        second.Texture.Should().BeSameAs(wrap);
        third.Texture.Should().BeSameAs(wrap);
    }

    [Fact]
    public void Distinct_urls_load_independently()
    {
        var calls = 0;
        using var cache = new GlamImageCache((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IDalamudTextureWrap?>(A.Fake<IDalamudTextureWrap>());
        });

        cache.Get(Url);
        cache.Get("https://glamours.ec/300/cover-0-1.png");
        cache.Get(Url);

        calls.Should().Be(2);
    }

    [Fact]
    public void Failed_fetch_yields_failed_state_and_does_not_retry()
    {
        var calls = 0;
        using var cache = new GlamImageCache((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IDalamudTextureWrap?>(null);
        });

        cache.Get(Url).State.Should().Be(GlamImageState.Failed);
        cache.Get(Url).State.Should().Be(GlamImageState.Failed);
        cache.Get(Url).State.Should().Be(GlamImageState.Failed);

        calls.Should().Be(1, "a failed load is cached, not retried on every frame");
    }

    [Fact]
    public void Loader_exception_is_swallowed_as_failed_and_not_retried()
    {
        var calls = 0;
        using var cache = new GlamImageCache((_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("boom");
        });

        WaitUntil(() => cache.Get(Url).State == GlamImageState.Failed);
        cache.Get(Url);
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_url_is_ignored(string? url)
    {
        var calls = 0;
        using var cache = new GlamImageCache((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IDalamudTextureWrap?>(A.Fake<IDalamudTextureWrap>());
        });

        cache.Get(url).State.Should().Be(GlamImageState.Failed);
        calls.Should().Be(0, "no load is kicked off for a missing URL");
    }

    [Fact]
    public void Loading_transitions_to_ready_when_the_load_completes()
    {
        var tcs = new TaskCompletionSource<IDalamudTextureWrap?>();
        using var cache = new GlamImageCache((_, _) => tcs.Task);

        cache.Get(Url).State.Should().Be(GlamImageState.Loading);

        var wrap = A.Fake<IDalamudTextureWrap>();
        tcs.SetResult(wrap);

        WaitUntil(() => cache.Get(Url).State == GlamImageState.Ready);
        cache.Get(Url).Texture.Should().BeSameAs(wrap);
    }

    [Fact]
    public void Get_after_dispose_returns_failed_and_does_not_load()
    {
        var calls = 0;
        var cache = new GlamImageCache((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IDalamudTextureWrap?>(A.Fake<IDalamudTextureWrap>());
        });

        cache.Dispose();

        cache.Get(Url).State.Should().Be(GlamImageState.Failed);
        calls.Should().Be(0);
    }

    [Fact]
    public void Dispose_disposes_all_owned_wraps()
    {
        var a = A.Fake<IDalamudTextureWrap>();
        var b = A.Fake<IDalamudTextureWrap>();
        var queue = new Queue<IDalamudTextureWrap>([a, b]);
        var cache = new GlamImageCache((_, _) => Task.FromResult<IDalamudTextureWrap?>(queue.Dequeue()));

        cache.Get(Url);
        cache.Get("https://glamours.ec/300/cover-0-1.png");
        cache.Dispose();

        A.CallTo(() => a.Dispose()).MustHaveHappenedOnceExactly();
        A.CallTo(() => b.Dispose()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Load_completing_after_dispose_disposes_its_own_wrap()
    {
        var tcs = new TaskCompletionSource<IDalamudTextureWrap?>();
        var cache = new GlamImageCache((_, _) => tcs.Task);

        cache.Get(Url).State.Should().Be(GlamImageState.Loading);
        cache.Dispose();

        // The load finishes after teardown: its texture must be freed here, not leaked.
        var wrap = A.Fake<IDalamudTextureWrap>();
        tcs.SetResult(wrap);

        WaitUntil(() =>
        {
            try
            {
                A.CallTo(() => wrap.Dispose()).MustHaveHappenedOnceExactly();
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var wrap = A.Fake<IDalamudTextureWrap>();
        var cache = new GlamImageCache((_, _) => Task.FromResult<IDalamudTextureWrap?>(wrap));
        cache.Get(Url);

        cache.Dispose();

        // A second Dispose must be a no-op, not throw (the CTS was already disposed) or re-dispose the
        // texture. Standard IDisposable contract: safe to call multiple times.
        cache.Invoking(c => c.Dispose()).Should().NotThrow();
        A.CallTo(() => wrap.Dispose()).MustHaveHappenedOnceExactly();
    }
}
