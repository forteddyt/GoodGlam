using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using GoodGlam.Diagnostics;
using GoodGlam.History;

namespace GoodGlam.Windows;

/// <summary>
/// The History tab of the unified <see cref="MainWindow"/>: a browsable, persistent table of every
/// qualifying drop. Each row shows its equipment piece, the selected glamour's loves count, the
/// item, a hover preview with rank navigation, a clickable glamour name that opens the selected
/// Eorzea Collection page, a link to all matching glamours, and an action that opens the captured
/// drop time and duty details. (Formerly the standalone HistoryWindow.)
/// </summary>
/// <remarks>
/// Rendering only. The link-vs-text decision lives in the tested <see cref="HistoryLinkCell"/>, the
/// open effect in the tested <see cref="HistoryActions"/>, the preview navigation/hover policy in the
/// pure helpers at the bottom of this file, the load scheduler in the tested <see cref="GlamImageCache"/>,
/// and the preview load flow (download/decode/upload + failure handling) in the tested
/// <see cref="GlamImageLoader"/>; this class just draws from and wires up those, so it is excluded
/// from coverage while the logic behind it is tested.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; the link decision (HistoryLinkCell), open effect (HistoryActions), preview policy helpers, image cache scheduler (GlamImageCache), and preview renderer/layout are extracted and tested, and a live ImGui context can't run in CI.")]
internal sealed class HistoryTab : IDisposable
{
    /// <summary>Logical longest-edge cap of the cover preview thumbnail, scaled by GlobalScale.</summary>
    private const float PreviewMaxSide = 320f;
    private const string DetailsBackdropId = "##GoodGlamDropDetailsBackdrop";
    internal static readonly string[] ColumnOrder =
        ["Piece", "Loves", "Item", "Preview", "Top Glam", "All Glams", "Details"];

    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// The production preview loader threaded into <see cref="GlamImageCache"/>, wired with the real
    /// <see cref="Http"/> client, the fully-managed <see cref="ImageSharpDecoder"/>, and Dalamud's
    /// <c>CreateFromRawAsync</c>. Decoding ourselves (rather than Dalamud's WIC-based
    /// <c>CreateFromImageAsync</c>) is what lets the WebP that Cloudflare Polish serves for Eorzea
    /// Collection cover images load under Wine — see issue #64. The load flow and its failure
    /// handling live in the tested <see cref="GlamImageLoader"/>; only this collaborator wiring (which
    /// needs a live HTTP client and render device) stays here. No <c>curl.exe</c>/OS sniffing;
    /// reaching Eorzea Collection without curl on native Windows is tracked separately.
    /// </summary>
    private static readonly GlamImageLoader Loader = new(
        (url, ct) => Http.GetByteArrayAsync(url, ct),
        new ImageSharpDecoder(),
        async (image, ct) => await Services.TextureProvider
            .CreateFromRawAsync(
                RawImageSpecification.Rgba32(image.Width, image.Height),
                image.Rgba,
                "GoodGlam.GlamImage",
                ct)
            .ConfigureAwait(false));

    private readonly NotificationHistoryStore store;
    private readonly ITraceLogger<HistoryTab> log = new TraceLogger<HistoryTab>();
    private readonly HistoryActions actions;
    private readonly GlamImageCache imageCache;
    private readonly GlamPreviewHoverState previewHover = new();
    private readonly IGlamPreviewCanvas previewCanvas = new ForegroundPreviewCanvas();
    private readonly DropDetailsWindow detailsWindow;

    internal HistoryTab(NotificationHistoryStore store)
        : this(store, new DalamudLinkOpener(), new GlamImageCache(LoadTextureAsync), new DropDetailsWindow())
    {
    }

    internal HistoryTab(NotificationHistoryStore store, DropDetailsWindow detailsWindow)
        : this(store, new DalamudLinkOpener(), new GlamImageCache(LoadTextureAsync), detailsWindow)
    {
    }

    internal HistoryTab(NotificationHistoryStore store, ILinkOpener linkOpener)
        : this(store, linkOpener, new GlamImageCache(LoadTextureAsync), new DropDetailsWindow())
    {
    }

    internal HistoryTab(NotificationHistoryStore store, ILinkOpener linkOpener, GlamImageCache imageCache)
        : this(store, linkOpener, imageCache, new DropDetailsWindow())
    {
    }

    internal HistoryTab(
        NotificationHistoryStore store,
        ILinkOpener linkOpener,
        GlamImageCache imageCache,
        DropDetailsWindow detailsWindow)
    {
        this.store = store;
        this.actions = new HistoryActions(linkOpener);
        this.imageCache = imageCache;
        this.detailsWindow = detailsWindow;
    }

    /// <summary>The ImGui layer the cover preview is painted on; must be the foreground so it floats above the window.</summary>
    internal PreviewLayer PreviewLayer => this.previewCanvas.Layer;

    internal void Draw()
    {
        var records = this.store.Snapshot();
        if (this.detailsWindow.Selected is not null &&
            !records.Any(record => ReferenceEquals(record, this.detailsWindow.Selected)))
        {
            this.detailsWindow.Close();
        }

        var overlayMin = ImGui.GetCursorScreenPos();
        var overlaySize = ImGui.GetContentRegionAvail();
        this.detailsWindow.SetHostBounds(overlayMin, overlaySize);

        var tableOpen = false;
        try
        {
            ImGui.TextDisabled($"{records.Count} qualifying drop(s) logged.");
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                this.log.Debug($"history Clear clicked ({records.Count} entries).");
                this.store.Clear();
            }

            ImGui.Separator();

            if (records.Count == 0)
            {
                ImGui.TextWrapped("No popular drops yet. Qualifying drops appear here, and persist across sessions.");
                return;
            }

            const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders
                | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;

            if (!ImGui.BeginTable("##history", 7, flags))
                return;

            tableOpen = true;
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn(ColumnOrder[0], ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn(ColumnOrder[1], ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn(ColumnOrder[2], ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(ColumnOrder[3], ImGuiTableColumnFlags.WidthFixed, 48);
            ImGui.TableSetupColumn(ColumnOrder[4], ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(ColumnOrder[5], ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn(ColumnOrder[6], ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableHeadersRow();

            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                ImGui.PushID(index);
                try
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(HistoryRecordPresentation.PieceLabel(record.Slot));

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(record.Loves.ToString());

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(record.ItemName);

                    ImGui.TableSetColumnIndex(3);
                    this.DrawImageIndicator(record);

                    ImGui.TableSetColumnIndex(4);
                    this.DrawLinkCell(record.GlamName ?? record.GlamUrl, record.GlamUrl, "(unknown)");

                    ImGui.TableSetColumnIndex(5);
                    this.DrawLinkCell("Browse", record.ListingUrl, "(n/a)");

                    ImGui.TableSetColumnIndex(6);
                    if (ImGui.SmallButton("View"))
                        this.detailsWindow.Show(record);
                }
                finally
                {
                    ImGui.PopID();
                }
            }
        }
        finally
        {
            if (tableOpen)
                ImGui.EndTable();

            this.DrawDetailsBackdrop(overlayMin, overlaySize);
            this.previewHover.EndFrame();
        }
    }

    internal void CloseDetails() => this.detailsWindow.Close();

    private void DrawDetailsBackdrop(Vector2 overlayMin, Vector2 overlaySize)
    {
        if (!this.detailsWindow.IsOpen)
            return;

        ImGui.SetNextWindowPos(overlayMin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(overlaySize, ImGuiCond.Always);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoNav;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0.55f));
        ImGui.Begin(DetailsBackdropId, flags);
        ImGui.PopStyleColor();
        ImGui.End();
    }

    /// <summary>
    /// Renders a cell as a clickable Eorzea Collection link. Falls back to plain/disabled text when
    /// there's no URL (e.g. older history entries saved before the link was captured). The decision
    /// is made by <see cref="HistoryLinkCell.Resolve"/>; this only paints it.
    /// </summary>
    private void DrawLinkCell(string? label, string? url, string fallback)
    {
        var cell = HistoryLinkCell.Resolve(label, url, fallback);

        if (cell.Kind == HistoryLinkKind.Disabled)
        {
            ImGui.TextDisabled(cell.Text);
            return;
        }

        if (cell.Kind == HistoryLinkKind.PlainText)
        {
            ImGui.TextUnformatted(cell.Text);
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudViolet);
        ImGui.TextUnformatted(cell.Text);
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(cell.Url!);
        }

        if (ImGui.IsItemClicked())
        {
            this.log.Debug($"opening Eorzea Collection link {cell.Url}.");
            this.actions.OpenLink(cell.Url);
        }
    }

    /// <summary>
    /// Draws the Preview-column image glyph. Hovering shows the selected glamour's anchored preview,
    /// while left/right click step through the ranked glamour list without wrapping and persist the new
    /// selection through <see cref="NotificationHistoryStore.UpdateSelectedIndex"/>.
    /// </summary>
    private void DrawImageIndicator(PopularDropRecord record)
    {
        var hasGlams = record.RankedGlams.Count > 0;
        var glyph = FontAwesomeIcon.Image.ToIconString();

        ImGui.PushFont(UiBuilder.IconFont);
        if (hasGlams)
            ImGui.TextUnformatted(glyph);
        else
            ImGui.TextDisabled(glyph);
        ImGui.PopFont();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            GlamSelectionNavigator.TryMove(record, GlamSelectionDirection.Next, this.store.UpdateSelectedIndex);
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            GlamSelectionNavigator.TryMove(record, GlamSelectionDirection.Previous, this.store.UpdateSelectedIndex);

        if (!ImGui.IsItemHovered())
            return;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var batch = this.previewHover.OnHover(record);
        if (batch.Count != 0)
            this.imageCache.SubmitBatch(batch);

        this.DrawAnchoredPreview(record, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    /// <summary>
    /// Draws the selected glamour's cover preview (or a small loading/absent note) anchored just beside
    /// the hovered icon, so it stays put instead of trailing the mouse like a default tooltip.
    /// Placement is decided by the pure <see cref="GlamPreviewLayout"/> and submission by
    /// <see cref="GlamPreviewRenderer"/> onto the foreground <see cref="previewCanvas"/>.
    /// </summary>
    private void DrawAnchoredPreview(PopularDropRecord record, Vector2 iconMin, Vector2 iconMax)
    {
        var image = this.imageCache.Get(record.GlamImageUrl);
        var ready = image is { State: GlamImageState.Ready, Texture: not null };
        var note = image.State == GlamImageState.Loading && !string.IsNullOrEmpty(record.GlamImageUrl)
            ? "Loading preview…"
            : "No preview available.";
        var header = GlamPreviewHeader.Create(record.ClampedSelectedIndex, record.RankedGlams.Count);
        var bodySize = ready ? PreviewSize(image.Texture!) : ImGui.CalcTextSize(note);
        var measurements = new GlamPreviewMeasurements(
            bodySize,
            ImGui.CalcTextSize(header.LeftHint.Text),
            ImGui.CalcTextSize(header.RankLabel.Text),
            ImGui.CalcTextSize(header.RightHint.Text));
        var box = GlamPreviewLayout.Compute(
            iconMin,
            iconMax,
            measurements,
            header,
            ImGui.GetIO().DisplaySize,
            ImGuiHelpers.GlobalScale);

        GlamPreviewRenderer.Render(this.previewCanvas, box, image, note);
    }

    /// <summary>Scales the cover to fit a square preview box (no upscaling), honouring the UI/DPI factor.</summary>
    private static Vector2 PreviewSize(IDalamudTextureWrap texture)
    {
        var native = texture.Size;
        var longest = MathF.Max(native.X, native.Y);
        if (longest <= 0f)
            return native;

        var scale = MathF.Min(1f, PreviewMaxSide / longest) * ImGuiHelpers.GlobalScale;
        return native * scale;
    }

    /// <summary>The texture loader threaded into <see cref="GlamImageCache"/>; delegates to the tested <see cref="GlamImageLoader"/>.</summary>
    private static Task<IDalamudTextureWrap?> LoadTextureAsync(string url, CancellationToken ct)
        => Loader.LoadAsync(url, ct);

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return http;
    }

    /// <summary>Releases the per-URL image textures owned by the cache when the window tears down.</summary>
    public void Dispose() => this.imageCache.Dispose();
}

internal enum GlamSelectionDirection
{
    Previous = -1,
    Next = 1,
}

/// <summary>Pure row-selection navigation: clamp-aware next/previous stepping with persistence delegation.</summary>
internal static class GlamSelectionNavigator
{
    internal static bool TryMove(PopularDropRecord record, GlamSelectionDirection direction, Func<Guid, int, bool> persist)
    {
        var count = record.RankedGlams.Count;
        if (count <= 1)
            return false;

        var next = record.ClampedSelectedIndex + (int)direction;
        if (next < 0 || next >= count)
            return false;

        return persist(record.RowId, next);
    }
}

/// <summary>
/// Pure preload policy for one row's selected rank. Hover entry queues the selected image first and
/// its clipped initial neighborhood; subsequent one-rank moves queue the selected image plus only the
/// newly exposed edge. Missing image URLs are skipped.
/// </summary>
internal static class GlamPreviewPreloadPolicy
{
    internal static IReadOnlyList<string> BuildInitialBatch(PopularDropRecord record)
    {
        if (record.RankedGlams.Count == 0)
            return [];

        var current = record.ClampedSelectedIndex;
        var start = Math.Max(0, current - 5);
        var end = Math.Min(record.RankedGlams.Count - 1, current == 0 ? 4 : current + 5);
        var urls = new List<string>(end - start + 1);

        Add(record, urls, current);
        for (var index = start; index <= end; index++)
        {
            if (index != current)
                Add(record, urls, index);
        }

        return urls;
    }

    internal static IReadOnlyList<string> BuildSelectionChangeBatch(PopularDropRecord record, int previousIndex)
    {
        if (record.RankedGlams.Count == 0)
            return [];

        var current = record.ClampedSelectedIndex;
        var previous = Math.Clamp(previousIndex, 0, record.RankedGlams.Count - 1);
        if (Math.Abs(current - previous) != 1)
            return BuildInitialBatch(record);

        var urls = new List<string>(2);
        Add(record, urls, current);

        var exposed = current > previous ? current + 4 : current - 5;
        if (exposed >= 0 && exposed < record.RankedGlams.Count && exposed != current)
            Add(record, urls, exposed);

        return urls;
    }

    private static void Add(PopularDropRecord record, List<string> urls, int index)
    {
        var url = record.RankedGlams[index].ImageUrl;
        if (!string.IsNullOrEmpty(url) && !urls.Contains(url, StringComparer.Ordinal))
            urls.Add(url);
    }
}

/// <summary>
/// Tracks hover entry for the preview icon. A batch is submitted only when a row is newly hovered,
/// re-entered after leaving, or its selected rank changes while still hovered; steady-state hover on
/// the same row/rank does not resubmit every frame.
/// </summary>
internal sealed class GlamPreviewHoverState
{
    private Guid? hoveredRowId;
    private int hoveredSelectedIndex = -1;
    private bool hoveredThisFrame;

    internal IReadOnlyList<string> OnHover(PopularDropRecord record)
    {
        this.hoveredThisFrame = true;
        var selectedIndex = record.ClampedSelectedIndex;
        IReadOnlyList<string> batch = [];
        if (this.hoveredRowId != record.RowId)
            batch = GlamPreviewPreloadPolicy.BuildInitialBatch(record);
        else if (this.hoveredSelectedIndex != selectedIndex)
            batch = GlamPreviewPreloadPolicy.BuildSelectionChangeBatch(record, this.hoveredSelectedIndex);

        this.hoveredRowId = record.RowId;
        this.hoveredSelectedIndex = selectedIndex;
        return batch;
    }

    internal void EndFrame()
    {
        if (!this.hoveredThisFrame)
        {
            this.hoveredRowId = null;
            this.hoveredSelectedIndex = -1;
        }

        this.hoveredThisFrame = false;
    }
}
