using System.Collections.Concurrent;
using Dalamud.Interface.Textures.TextureWraps;
using GoodGlam.Diagnostics;

namespace GoodGlam.Windows;

/// <summary>The load state of a glamour cover image for one URL.</summary>
internal enum GlamImageState
{
    /// <summary>The image is still downloading/decoding (or hasn't been requested yet).</summary>
    Loading,

    /// <summary>The image is decoded and its texture is ready to draw.</summary>
    Ready,

    /// <summary>No image (null/empty URL) or the load failed; nothing to draw.</summary>
    Failed,
}

/// <summary>A point-in-time view of a cached image: its state and, when <see cref="GlamImageState.Ready"/>, its texture.</summary>
internal readonly record struct GlamImage(GlamImageState State, IDalamudTextureWrap? Texture);

/// <summary>
/// A lazy, per-URL, load-once cache of glamour cover images for the History tab's hover preview.
/// Each distinct URL is fetched exactly once via an injected loader; the resulting GPU texture is
/// cached for the plugin's lifetime and disposed on teardown, so a hover doesn't refetch every frame.
/// In-memory only — nothing is written to disk (a hovered image simply reloads after a plugin
/// reload). The loader is injected so the request-once/cache/dispose logic is unit-tested without a
/// live render device or network.
/// </summary>
internal sealed class GlamImageCache : IDisposable
{
    private readonly Func<string, CancellationToken, Task<IDalamudTextureWrap?>> loader;
    private readonly ITraceLogger<GlamImageCache> log;
    private readonly ConcurrentDictionary<string, Lazy<Entry>> entries = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cts = new();
    private volatile bool disposed;

    internal GlamImageCache(
        Func<string, CancellationToken, Task<IDalamudTextureWrap?>> loader,
        ITraceLogger<GlamImageCache>? log = null)
    {
        this.loader = loader;
        this.log = log ?? new TraceLogger<GlamImageCache>();
    }

    /// <summary>
    /// Returns the current state/texture for <paramref name="url"/>, kicking off a one-time async
    /// load on first sight. A null/empty URL (older history / no scraped image) or a request after
    /// disposal yields <see cref="GlamImageState.Failed"/> with no texture and no load.
    /// </summary>
    public GlamImage Get(string? url)
    {
        if (string.IsNullOrEmpty(url) || this.disposed)
            return new GlamImage(GlamImageState.Failed, null);

        var entry = this.entries.GetOrAdd(url, u => new Lazy<Entry>(() => Entry.Start(this, u)));
        return entry.Value.Snapshot();
    }

    /// <summary>Disposes every owned texture, cancelling any in-flight loads first. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.cts.Cancel();

        foreach (var entry in this.entries.Values)
        {
            if (entry.IsValueCreated)
                entry.Value.Dispose();
        }

        this.entries.Clear();
        this.cts.Dispose();
    }

    /// <summary>
    /// A single URL's cache slot. Kicks its load off once on creation, then publishes the resulting
    /// texture (or a failed state) under a lock that races <see cref="Dispose"/> exactly once — so a
    /// load finishing after the cache is torn down frees its own texture instead of leaking it
    /// (mirrors the publish-or-dispose hand-off in <see cref="LogoWindow"/>).
    /// </summary>
    private sealed class Entry
    {
        private readonly object gate = new();
        private GlamImageState state = GlamImageState.Loading;
        private IDalamudTextureWrap? texture;
        private bool disposed;

        public static Entry Start(GlamImageCache cache, string url)
        {
            var entry = new Entry();
            _ = entry.LoadAsync(cache, url);
            return entry;
        }

        public GlamImage Snapshot()
        {
            lock (this.gate)
                return new GlamImage(this.state, this.texture);
        }

        public void Dispose()
        {
            IDalamudTextureWrap? toDispose;
            lock (this.gate)
            {
                this.disposed = true;
                toDispose = this.texture;
                this.texture = null;
            }

            toDispose?.Dispose();
        }

        private async Task LoadAsync(GlamImageCache cache, string url)
        {
            IDalamudTextureWrap? wrap = null;
            try
            {
                cache.log.Verbose($"loading glam image '{url}'.");
                wrap = await cache.loader(url, cache.cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cache.log.Warning($"failed to load glam image '{url}'.", ex);
            }

            lock (this.gate)
            {
                // The cache (or this entry) was torn down while this load was in flight: free our own
                // texture rather than publish it to a disposed cache (which would never dispose it).
                // Checking the cache-level flag too closes the race where a Get() slips past the
                // disposed guard and starts a fresh load after Dispose() has already swept the entries.
                if (this.disposed || cache.disposed)
                {
                    wrap?.Dispose();
                    return;
                }

                this.texture = wrap;
                this.state = wrap is null ? GlamImageState.Failed : GlamImageState.Ready;
            }
        }
    }
}
