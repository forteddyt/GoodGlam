using System.Net;
using System.Numerics;
using System.Diagnostics.CodeAnalysis;
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
/// qualifying drop. Each row shows when it dropped, the item, the top loves count, a clickable
/// glamour name that opens the Eorzea Collection page, and — new — a hover preview of the top
/// glamour's cover image. (Formerly the standalone HistoryWindow.)
/// </summary>
/// <remarks>
/// Rendering only. The link-vs-text decision lives in the tested <see cref="HistoryLinkCell"/>, the
/// open effect in the tested <see cref="HistoryActions"/>, and the lazy per-URL image loading in the
/// tested <see cref="GlamImageCache"/>; this class just draws from those, so it is excluded from
/// coverage while the logic behind it is tested.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; the link decision (HistoryLinkCell), open effect (HistoryActions), and image cache (GlamImageCache) are extracted and tested, and a live ImGui context can't run in CI.")]
internal sealed class HistoryTab : IDisposable
{
    /// <summary>Logical longest-edge cap of the cover preview thumbnail, scaled by GlobalScale.</summary>
    private const float PreviewMaxSide = 320f;

    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>Static loader collaborators: the static <see cref="LoadTextureAsync"/> can't use the instance log/decoder.</summary>
    private static readonly ITraceLogger<HistoryTab> LoaderLog = new TraceLogger<HistoryTab>();
    private static readonly IGlamImageDecoder Decoder = new ImageSharpDecoder();

    private readonly NotificationHistoryStore store;
    private readonly ITraceLogger<HistoryTab> log = new TraceLogger<HistoryTab>();
    private readonly HistoryActions actions;
    private readonly GlamImageCache imageCache;
    private readonly IGlamPreviewCanvas previewCanvas = new ForegroundPreviewCanvas();

    internal HistoryTab(NotificationHistoryStore store)
        : this(store, new DalamudLinkOpener())
    {
    }

    internal HistoryTab(NotificationHistoryStore store, ILinkOpener linkOpener)
        : this(store, linkOpener, new GlamImageCache(LoadTextureAsync))
    {
    }

    internal HistoryTab(NotificationHistoryStore store, ILinkOpener linkOpener, GlamImageCache imageCache)
    {
        this.store = store;
        this.actions = new HistoryActions(linkOpener);
        this.imageCache = imageCache;
    }

    /// <summary>The ImGui layer the cover preview is painted on; must be the foreground so it floats above the window.</summary>
    internal PreviewLayer PreviewLayer => this.previewCanvas.Layer;

    internal void Draw()
    {
        var records = this.store.Snapshot();

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

        if (!ImGui.BeginTable("##history", 6, flags))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthFixed, 130);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Loves", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Top Glam", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed, 48);
        ImGui.TableSetupColumn("All Glams", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();

        foreach (var record in records)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(record.Timestamp.ToLocalTime().ToString("g"));

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(record.ItemName);

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(record.Loves.ToString());

            ImGui.TableSetColumnIndex(3);
            this.DrawLinkCell(record.GlamName ?? record.GlamUrl, record.GlamUrl, "(unknown)");

            // A small image glyph whose hover reveals the top glam's cover preview.
            ImGui.TableSetColumnIndex(4);
            this.DrawImageIndicator(record.GlamImageUrl);

            ImGui.TableSetColumnIndex(5);
            this.DrawLinkCell("Browse", record.ListingUrl, "(n/a)");
        }

        ImGui.EndTable();
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
    /// Draws the Image-column indicator: a small FontAwesome image glyph, coloured when a cover image
    /// was captured and dimmed when not. Hovering the lit glyph shows the top glamour's cover image in
    /// a preview anchored beside the icon (not following the cursor); the image is fetched lazily and
    /// cached per URL by <see cref="GlamImageCache"/>.
    /// </summary>
    private void DrawImageIndicator(string? imageUrl)
    {
        var hasImage = !string.IsNullOrEmpty(imageUrl);
        var glyph = FontAwesomeIcon.Image.ToIconString();

        ImGui.PushFont(UiBuilder.IconFont);
        if (hasImage)
            ImGui.TextUnformatted(glyph);
        else
            ImGui.TextDisabled(glyph);
        ImGui.PopFont();

        if (hasImage && ImGui.IsItemHovered())
            this.DrawAnchoredPreview(imageUrl, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    /// <summary>
    /// Draws the cover preview (or a small loading/absent note) anchored just beside the hovered icon,
    /// so it stays put instead of trailing the mouse like a default tooltip. Placement is decided by
    /// the pure <see cref="GlamPreviewLayout"/> and submission by <see cref="GlamPreviewRenderer"/>
    /// onto the foreground <see cref="previewCanvas"/> (so it floats above the window); this method
    /// only measures the content (which needs live ImGui) and hands off. The texture is fetched
    /// lazily and cached per URL by <see cref="GlamImageCache"/>, so hovering doesn't refetch each frame.
    /// </summary>
    private void DrawAnchoredPreview(string? imageUrl, Vector2 iconMin, Vector2 iconMax)
    {
        var image = this.imageCache.Get(imageUrl);
        var ready = image is { State: GlamImageState.Ready, Texture: not null };
        var note = image.State == GlamImageState.Loading ? "Loading preview…" : "No preview available.";

        var contentSize = ready ? PreviewSize(image.Texture!) : ImGui.CalcTextSize(note);
        var box = GlamPreviewLayout.Compute(iconMin, iconMax, contentSize, ImGui.GetIO().DisplaySize, ImGuiHelpers.GlobalScale);

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

    /// <summary>
    /// The production texture loader threaded into <see cref="GlamImageCache"/>: download the image
    /// bytes with the managed HTTP client, decode them to raw RGBA with the fully-managed
    /// <see cref="ImageSharpDecoder"/>, and upload via <c>CreateFromRawAsync</c>. Decoding ourselves
    /// (rather than Dalamud's WIC-based <c>CreateFromImageAsync</c>) is what lets the WebP that
    /// Cloudflare Polish serves for Eorzea Collection cover images load under Wine — see issue #64.
    /// Returns <c>null</c> on any failure (network, empty/blocked response, decode, or GPU upload),
    /// which the cache treats as a missing preview — the row still renders, just without an image.
    /// Each failure logs its distinct reason so a future report is diagnosable from the xllogs alone,
    /// instead of the old silent null. No <c>curl.exe</c>/OS sniffing here; reaching Eorzea Collection
    /// without curl on native Windows is tracked separately.
    /// </summary>
    private static async Task<IDalamudTextureWrap?> LoadTextureAsync(string url, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoaderLog.Warning($"preview image download failed for '{url}'.", ex);
            return null;
        }

        if (bytes.Length == 0)
        {
            LoaderLog.Warning($"preview image download for '{url}' returned no bytes (empty/blocked response).");
            return null;
        }

        DecodedImage decoded;
        try
        {
            decoded = Decoder.Decode(bytes);
        }
        catch (Exception ex)
        {
            LoaderLog.Warning($"preview image decode failed for '{url}' ({bytes.Length} bytes).", ex);
            return null;
        }

        try
        {
            return await Services.TextureProvider
                .CreateFromRawAsync(
                    RawImageSpecification.Rgba32(decoded.Width, decoded.Height),
                    decoded.Rgba,
                    "GoodGlam.GlamImage",
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoaderLog.Warning($"preview image GPU upload failed for '{url}' ({decoded.Width}x{decoded.Height}).", ex);
            return null;
        }
    }

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
