using System.Diagnostics;
using Dalamud.Interface.Textures.TextureWraps;
using FakeItEasy;
using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

public class GlamImageCacheTests
{
    public GlamImageCacheTests() => TestServices.EnsureLog();

    private const string Url = "https://glamours.ec/200/cover-0-9.png";

    private static void WaitUntil(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < TimeSpan.FromSeconds(2))
            Thread.Sleep(5);
        condition().Should().BeTrue("the awaited condition should hold within the timeout");
    }

    [Fact]
    public void Snapshot_is_inert_until_a_batch_is_submitted()
    {
        var calls = 0;
        using var cache = new GlamImageCache((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IDalamudTextureWrap?>(A.Fake<IDalamudTextureWrap>());
        });

        _ = cache.Get(Url);

        calls.Should().Be(0, "row display alone must not start preloading before the first hover submission");

        cache.SubmitBatch([Url]);

        WaitUntil(() => calls == 1);
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

        cache.SubmitBatch([Url]);

        WaitUntil(() => cache.Get(Url).State == GlamImageState.Ready);
        var first = cache.Get(Url);
        var second = cache.Get(Url);
        var third = cache.Get(Url);

        calls.Should().Be(1, "a URL is fetched exactly once and then served from cache");
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

        cache.SubmitBatch([Url, UrlFor(2)]);
        WaitUntil(() => calls == 2);
        cache.SubmitBatch([Url]);

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

        cache.SubmitBatch([Url]);

        WaitUntil(() => cache.Get(Url).State == GlamImageState.Failed);
        cache.SubmitBatch([Url]);
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

        cache.SubmitBatch([Url]);

        WaitUntil(() => cache.Get(Url).State == GlamImageState.Failed);
        cache.SubmitBatch([Url]);
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

        cache.SubmitBatch([url]);

        cache.Get(url).State.Should().Be(GlamImageState.Failed);
        calls.Should().Be(0, "no load is kicked off for a missing URL");
    }

    [Fact]
    public void Loading_transitions_to_ready_when_the_load_completes()
    {
        var tcs = new TaskCompletionSource<IDalamudTextureWrap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = new GlamImageCache((_, _) => tcs.Task);

        cache.SubmitBatch([Url]);
        cache.Get(Url).State.Should().Be(GlamImageState.Loading);

        var wrap = A.Fake<IDalamudTextureWrap>();
        tcs.SetResult(wrap);

        WaitUntil(() => cache.Get(Url).State == GlamImageState.Ready);
        cache.Get(Url).Texture.Should().BeSameAs(wrap);
    }

    [Fact]
    public void Scheduler_caps_active_loads_at_five_and_only_starts_the_sixth_after_completion()
    {
        var loader = new ControlledLoader();
        using var cache = new GlamImageCache(loader.Load);
        var urls = Enumerable.Range(1, 6).Select(UrlFor).ToArray();

        cache.SubmitBatch(urls);

        WaitUntil(() => loader.Started.Count == 5);
        loader.MaxActive.Should().Be(5);
        loader.Started.Should().Equal(urls.Take(5));

        loader.Complete(urls[0]);

        WaitUntil(() => loader.Started.Count == 6);
        loader.Started[5].Should().Be(urls[5]);
        loader.MaxActive.Should().Be(5);
    }

    [Fact]
    public void Newest_submission_reprioritizes_pending_urls_without_canceling_active_loads()
    {
        var loader = new ControlledLoader();
        using var cache = new GlamImageCache(loader.Load);
        var older = Enumerable.Range(1, 6).Select(UrlFor).ToArray();
        var newer = new[] { UrlFor(101), UrlFor(102) };

        cache.SubmitBatch(older);
        WaitUntil(() => loader.Started.Count == 5);

        cache.SubmitBatch(newer);
        loader.Cancelled.Should().BeEmpty();
        loader.Started.Should().Equal(older.Take(5));

        loader.Complete(older[0]);
        WaitUntil(() => loader.Started.Count == 6);
        loader.Started[5].Should().Be(newer[0]);

        loader.Complete(older[1]);
        WaitUntil(() => loader.Started.Count == 7);
        loader.Started[6].Should().Be(newer[1]);
        loader.Cancelled.Should().BeEmpty();
    }

    [Fact]
    public void Resubmitting_a_pending_url_reprioritizes_it_without_starting_a_duplicate_load()
    {
        var loader = new ControlledLoader();
        using var cache = new GlamImageCache(loader.Load);
        var older = Enumerable.Range(1, 6).Select(UrlFor).ToArray();
        var reprioritized = older[5];
        var companion = UrlFor(200);

        cache.SubmitBatch(older);
        WaitUntil(() => loader.Started.Count == 5);

        cache.SubmitBatch([reprioritized, companion]);

        loader.Complete(older[0]);
        WaitUntil(() => loader.Started.Count == 6);
        loader.Started[5].Should().Be(reprioritized);

        loader.Complete(older[1]);
        WaitUntil(() => loader.Started.Count == 7);
        loader.Started[6].Should().Be(companion);
        loader.Started.Count(url => url == reprioritized).Should().Be(1);
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
        cache.SubmitBatch([Url]);

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

        cache.SubmitBatch([Url, UrlFor(2)]);
        WaitUntil(() => cache.Get(Url).State == GlamImageState.Ready && cache.Get(UrlFor(2)).State == GlamImageState.Ready);
        cache.Dispose();

        A.CallTo(() => a.Dispose()).MustHaveHappenedOnceExactly();
        A.CallTo(() => b.Dispose()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Load_completing_after_dispose_disposes_its_own_wrap()
    {
        var tcs = new TaskCompletionSource<IDalamudTextureWrap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new GlamImageCache((_, _) => tcs.Task);

        cache.SubmitBatch([Url]);
        cache.Get(Url).State.Should().Be(GlamImageState.Loading);
        cache.Dispose();

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
        cache.SubmitBatch([Url]);
        WaitUntil(() => cache.Get(Url).State == GlamImageState.Ready);

        cache.Dispose();

        cache.Invoking(c => c.Dispose()).Should().NotThrow();
        A.CallTo(() => wrap.Dispose()).MustHaveHappenedOnceExactly();
    }

    private static string UrlFor(int id) => $"https://glamours.ec/{id}/cover.png";

    private sealed class ControlledLoader
    {
        private readonly object gate = new();
        private readonly Dictionary<string, TaskCompletionSource<IDalamudTextureWrap?>> loads = new(StringComparer.Ordinal);
        private readonly List<string> started = [];
        private readonly List<string> cancelled = [];
        private int active;
        private int maxActive;

        public IReadOnlyList<string> Started
        {
            get
            {
                lock (this.gate)
                    return this.started.ToArray();
            }
        }

        public IReadOnlyList<string> Cancelled
        {
            get
            {
                lock (this.gate)
                    return this.cancelled.ToArray();
            }
        }

        public int MaxActive => Volatile.Read(ref this.maxActive);

        public Task<IDalamudTextureWrap?> Load(string url, CancellationToken ct)
        {
            lock (this.gate)
            {
                this.started.Add(url);
                this.loads[url] = new TaskCompletionSource<IDalamudTextureWrap?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            this.UpdateMax(Interlocked.Increment(ref this.active));
            return this.Await(url, ct);
        }

        public void Complete(string url, IDalamudTextureWrap? wrap = null)
        {
            lock (this.gate)
                this.loads[url].TrySetResult(wrap ?? A.Fake<IDalamudTextureWrap>());
        }

        private async Task<IDalamudTextureWrap?> Await(string url, CancellationToken ct)
        {
            Task<IDalamudTextureWrap?> task;
            TaskCompletionSource<IDalamudTextureWrap?> tcs;
            lock (this.gate)
            {
                tcs = this.loads[url];
                task = tcs.Task;
            }

            using var registration = ct.Register(() =>
            {
                lock (this.gate)
                    this.cancelled.Add(url);
                tcs.TrySetCanceled(ct);
            });

            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref this.active);
            }
        }

        private void UpdateMax(int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref this.maxActive);
                if (candidate <= current)
                    return;

                if (Interlocked.CompareExchange(ref this.maxActive, candidate, current) == current)
                    return;
            }
        }
    }
}
