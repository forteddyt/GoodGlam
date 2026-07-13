using Dalamud.Interface.Textures.TextureWraps;
using GoodGlam.Diagnostics;

namespace GoodGlam.Windows;

/// <summary>The load state of a glamour cover image for one URL.</summary>
internal enum GlamImageState
{
    /// <summary>The image is still queued/downloading/decoding (or hasn't been requested yet).</summary>
    Loading,

    /// <summary>The image is decoded and its texture is ready to draw.</summary>
    Ready,

    /// <summary>No image (null/empty URL) or the load failed; nothing to draw.</summary>
    Failed,
}

/// <summary>A point-in-time view of a cached image: its state and, when <see cref="GlamImageState.Ready"/>, its texture.</summary>
internal readonly record struct GlamImage(GlamImageState State, IDalamudTextureWrap? Texture);

/// <summary>
/// A lazy, per-URL, load-once cache and scheduler of glamour cover images for the History tab's hover
/// preview. Callers submit ordered preload batches on hover entry/selection changes; the newest batch
/// wins pending priority, while already-active loads are left alone. At most five loader invocations
/// run at once across the whole History tab, and textures are disposed on teardown.
/// </summary>
internal sealed class GlamImageCache : IDisposable
{
    private const int MaxConcurrentLoads = 5;

    private readonly Func<string, CancellationToken, Task<IDalamudTextureWrap?>> loader;
    private readonly ITraceLogger<GlamImageCache> log;
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cts = new();
    private long submissionSequence;
    private int activeLoads;
    private bool disposed;

    internal GlamImageCache(
        Func<string, CancellationToken, Task<IDalamudTextureWrap?>> loader,
        ITraceLogger<GlamImageCache>? log = null)
    {
        this.loader = loader;
        this.log = log ?? new TraceLogger<GlamImageCache>();
    }

    /// <summary>
    /// Returns the current snapshot for <paramref name="url"/> without scheduling a load. A null/empty
    /// URL or a request after disposal yields <see cref="GlamImageState.Failed"/> with no texture.
    /// Unseen URLs report <see cref="GlamImageState.Loading"/> until a hover submission queues them.
    /// </summary>
    public GlamImage Get(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return new GlamImage(GlamImageState.Failed, null);

        lock (this.gate)
        {
            if (this.disposed)
                return new GlamImage(GlamImageState.Failed, null);

            return this.entries.TryGetValue(url, out var entry)
                ? entry.Snapshot()
                : new GlamImage(GlamImageState.Loading, null);
        }
    }

    /// <summary>
    /// Submits a hover-ordered preload batch. Missing URLs are skipped, queued URLs are deduplicated,
    /// ready/failed URLs stay load-once, pending URLs are reprioritized to this newest batch, and
    /// active loads keep running without cancellation or preemption.
    /// </summary>
    public void SubmitBatch(IEnumerable<string?> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);

        List<Entry> toStart;
        lock (this.gate)
        {
            if (this.disposed)
                return;

            var batch = ++this.submissionSequence;
            var order = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var url in urls)
            {
                if (string.IsNullOrEmpty(url) || !seen.Add(url))
                    continue;

                if (!this.entries.TryGetValue(url, out var entry))
                {
                    entry = new Entry(url);
                    this.entries.Add(url, entry);
                }

                if (entry.State is EntryState.Ready or EntryState.Failed or EntryState.Active)
                {
                    order++;
                    continue;
                }

                entry.Submission = batch;
                entry.Order = order++;
                entry.State = EntryState.Pending;
            }

            toStart = this.StartPendingLocked();
        }

        this.StartLoads(toStart);
    }

    /// <summary>Disposes every owned texture, cancelling any in-flight loads first. Safe to call more than once.</summary>
    public void Dispose()
    {
        List<IDalamudTextureWrap> toDispose;
        lock (this.gate)
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.cts.Cancel();
            toDispose = this.entries.Values
                .Select(entry => entry.TakeTexture())
                .Where(texture => texture is not null)
                .Cast<IDalamudTextureWrap>()
                .ToList();
            this.entries.Clear();
        }

        foreach (var texture in toDispose)
            texture.Dispose();

        this.cts.Dispose();
    }

    private List<Entry> StartPendingLocked()
    {
        var toStart = new List<Entry>();
        if (this.disposed)
            return toStart;

        while (this.activeLoads < MaxConcurrentLoads)
        {
            Entry? next = null;
            foreach (var entry in this.entries.Values)
            {
                if (entry.State != EntryState.Pending)
                    continue;

                if (next is null
                    || entry.Submission > next.Submission
                    || (entry.Submission == next.Submission && entry.Order < next.Order))
                {
                    next = entry;
                }
            }

            if (next is null)
                break;

            next.State = EntryState.Active;
            this.activeLoads++;
            toStart.Add(next);
        }

        return toStart;
    }

    private void StartLoads(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries)
            _ = this.LoadAsync(entry);
    }

    private async Task LoadAsync(Entry entry)
    {
        IDalamudTextureWrap? wrap = null;
        try
        {
            this.log.Verbose($"loading glam image '{entry.Url}'.");
            wrap = await this.loader(entry.Url, this.cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.log.Warning($"failed to load glam image '{entry.Url}'.", ex);
        }

        List<Entry> toStart;
        lock (this.gate)
        {
            if (entry.State == EntryState.Active)
                this.activeLoads--;

            if (this.disposed)
            {
                toStart = [];
            }
            else
            {
                entry.Texture = wrap;
                entry.State = wrap is null ? EntryState.Failed : EntryState.Ready;
                toStart = this.StartPendingLocked();
                wrap = null;
            }
        }

        wrap?.Dispose();
        this.StartLoads(toStart);
    }

    private enum EntryState
    {
        Pending,
        Active,
        Ready,
        Failed,
    }

    /// <summary>One URL's scheduler state and eventual texture snapshot.</summary>
    private sealed class Entry(string url)
    {
        public string Url { get; } = url;

        public EntryState State { get; set; } = EntryState.Pending;

        public IDalamudTextureWrap? Texture { get; set; }

        public long Submission { get; set; }

        public int Order { get; set; }

        public GlamImage Snapshot()
            => new(
                this.State == EntryState.Ready ? GlamImageState.Ready : this.State == EntryState.Failed ? GlamImageState.Failed : GlamImageState.Loading,
                this.Texture);

        public IDalamudTextureWrap? TakeTexture()
        {
            var texture = this.Texture;
            this.Texture = null;
            return texture;
        }
    }
}
