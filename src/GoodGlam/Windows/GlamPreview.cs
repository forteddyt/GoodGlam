using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace GoodGlam.Windows;

/// <summary>
/// Which ImGui layer the glam cover preview is painted onto. It must be
/// <see cref="Foreground"/> so the preview floats above the GoodGlam window — a plain window (or a
/// window-local draw list) renders <em>behind</em> the focused main window, which is the exact
/// regression this type exists to pin down.
/// </summary>
internal enum PreviewLayer
{
    /// <summary>The per-window draw list — renders behind other, higher windows. Do not use for the preview.</summary>
    Window,

    /// <summary>The global foreground draw list — painted after every window, so it's always on top.</summary>
    Foreground,
}

/// <summary>A single header label before layout assigns it a position.</summary>
internal readonly record struct GlamPreviewLabel(string Text, bool Enabled);

/// <summary>The header chrome above the preview body: navigation hints plus the current rank label.</summary>
internal readonly record struct GlamPreviewHeaderModel(
    GlamPreviewLabel LeftHint,
    GlamPreviewLabel RankLabel,
    GlamPreviewLabel RightHint);

/// <summary>Builds the header chrome for the current selected glamour rank.</summary>
internal static class GlamPreviewHeader
{
    internal const string LeftHintText = "← right click";
    internal const string RightHintText = "left click →";

    internal static GlamPreviewHeaderModel Create(int selectedIndex, int glamCount)
    {
        var hasSelection = glamCount > 0;
        var clampedIndex = hasSelection ? Math.Clamp(selectedIndex, 0, glamCount - 1) : 0;
        var rank = hasSelection ? clampedIndex + 1 : 0;
        var movable = glamCount > 1;

        return new GlamPreviewHeaderModel(
            new GlamPreviewLabel(LeftHintText, movable && clampedIndex > 0),
            new GlamPreviewLabel($"Rank #{rank}", true),
            new GlamPreviewLabel(RightHintText, movable && clampedIndex < glamCount - 1));
    }
}

/// <summary>Measured body/header sizes for a preview frame, gathered via live ImGui text/image measurement.</summary>
internal readonly record struct GlamPreviewMeasurements(
    Vector2 BodySize,
    Vector2 LeftHintSize,
    Vector2 RankLabelSize,
    Vector2 RightHintSize);

/// <summary>A laid-out text label with its on-screen position and enabled/disabled state.</summary>
internal readonly record struct GlamPreviewPlacedLabel(string Text, Vector2 Position, bool Enabled);

/// <summary>
/// The resolved on-screen rectangle for a glam cover preview: the outer box (background/border), the
/// header labels above the body, and the final body rect for either the image or the status note.
/// Pure geometry, so it's unit-testable without a live ImGui context.
/// </summary>
internal readonly record struct GlamPreviewBox(
    Vector2 Min,
    Vector2 Max,
    GlamPreviewPlacedLabel LeftHint,
    GlamPreviewPlacedLabel RankLabel,
    GlamPreviewPlacedLabel RightHint,
    Vector2 BodyMin,
    Vector2 BodySize);

/// <summary>
/// Pure placement maths for the cover preview: anchors the box below and beside the hovered icon so
/// the rest of its History row stays visible, flipping above or left when the preferred placement
/// would overflow the display. It also reserves header width so the navigation hints don't overlap
/// the rank label and centers the image/note when the body is narrower than the header chrome.
/// </summary>
internal static class GlamPreviewLayout
{
    internal const float Gap = 8f;
    internal const float VerticalGap = 3f;
    internal const float Padding = 6f;
    internal const float HeaderGap = 8f;
    internal const float HeaderBodyGap = 6f;

    internal static GlamPreviewBox Compute(
        Vector2 iconMin,
        Vector2 iconMax,
        GlamPreviewMeasurements measurements,
        GlamPreviewHeaderModel header,
        Vector2 displaySize,
        float scale)
    {
        var gap = Gap * scale;
        var verticalGap = VerticalGap * scale;
        var padding = Padding * scale;
        var headerGap = HeaderGap * scale;
        var headerHeight = MathF.Max(measurements.LeftHintSize.Y, MathF.Max(measurements.RankLabelSize.Y, measurements.RightHintSize.Y));
        var bodyGap = headerHeight > 0f && measurements.BodySize.Y > 0f ? HeaderBodyGap * scale : 0f;
        var contentWidth = MathF.Max(measurements.BodySize.X, RequiredHeaderWidth(measurements, headerGap));
        var contentHeight = headerHeight + bodyGap + measurements.BodySize.Y;
        var boxSize = new Vector2(contentWidth + (padding * 2f), contentHeight + (padding * 2f));

        var x = iconMax.X + gap;
        if (x + boxSize.X > displaySize.X)
            x = iconMin.X - gap - boxSize.X;

        var y = iconMax.Y + verticalGap;
        if (y + boxSize.Y > displaySize.Y)
            y = iconMin.Y - verticalGap - boxSize.Y;

        var min = new Vector2(
            Clamp(x, displaySize.X - boxSize.X),
            Clamp(y, displaySize.Y - boxSize.Y));
        var contentMin = min + new Vector2(padding, padding);
        var leftPosition = contentMin;
        var rankPosition = new Vector2(contentMin.X + ((contentWidth - measurements.RankLabelSize.X) / 2f), contentMin.Y);
        var rightPosition = new Vector2(contentMin.X + contentWidth - measurements.RightHintSize.X, contentMin.Y);
        var bodyMin = new Vector2(
            contentMin.X + ((contentWidth - measurements.BodySize.X) / 2f),
            contentMin.Y + headerHeight + bodyGap);

        return new GlamPreviewBox(
            min,
            min + boxSize,
            new GlamPreviewPlacedLabel(header.LeftHint.Text, leftPosition, header.LeftHint.Enabled),
            new GlamPreviewPlacedLabel(header.RankLabel.Text, rankPosition, header.RankLabel.Enabled),
            new GlamPreviewPlacedLabel(header.RightHint.Text, rightPosition, header.RightHint.Enabled),
            bodyMin,
            measurements.BodySize);

        static float Clamp(float value, float max) => Math.Max(0f, Math.Min(value, max));

        static float RequiredHeaderWidth(GlamPreviewMeasurements measurements, float headerGap)
            => MathF.Max(
                measurements.RankLabelSize.X + (2f * (measurements.LeftHintSize.X + headerGap)),
                measurements.RankLabelSize.X + (2f * (measurements.RightHintSize.X + headerGap)));
    }
}

/// <summary>
/// The surface the preview paints onto, abstracted so the render flow (which layer, background,
/// header, and image-vs-note) is testable with a fake, while the real ImGui submission stays in
/// <see cref="ForegroundPreviewCanvas"/>. <see cref="Layer"/> declares which ImGui layer the canvas
/// draws to, so a test can assert the preview is on the foreground.
/// </summary>
internal interface IGlamPreviewCanvas
{
    PreviewLayer Layer { get; }

    void Background(GlamPreviewBox box);

    void Header(GlamPreviewPlacedLabel segment);

    void Image(ImTextureID handle, Vector2 min, Vector2 max);

    void Note(Vector2 pos, string text);
}

/// <summary>
/// Chooses what to paint for a preview: always a background/border, then the navigation/rank header,
/// then either the cover image (when ready) or a short status note (loading / unavailable). Pure
/// decision flow over an <see cref="IGlamPreviewCanvas"/>, so it's unit-tested with a fake canvas.
/// </summary>
internal static class GlamPreviewRenderer
{
    internal static void Render(IGlamPreviewCanvas canvas, GlamPreviewBox box, GlamImage image, string note)
    {
        canvas.Background(box);
        canvas.Header(box.LeftHint);
        canvas.Header(box.RankLabel);
        canvas.Header(box.RightHint);

        if (image is { State: GlamImageState.Ready, Texture: { } texture })
            canvas.Image(texture.Handle, box.BodyMin, box.BodyMin + box.BodySize);
        else
            canvas.Note(box.BodyMin, note);
    }
}

/// <summary>
/// Production canvas: paints the preview onto ImGui's <em>foreground</em> draw list so it always sits
/// above the GoodGlam window. Only the ImGui submission lives here (uncovered — no live context in
/// CI); the layer choice and render flow are what the tested seams pin down.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin ImGui foreground-draw-list submission; needs a live ImGui context that can't run in CI. The layer choice and render flow are tested via IGlamPreviewCanvas/GlamPreviewRenderer.")]
internal sealed class ForegroundPreviewCanvas : IGlamPreviewCanvas
{
    public PreviewLayer Layer => PreviewLayer.Foreground;

    public void Background(GlamPreviewBox box)
    {
        var drawList = ImGui.GetForegroundDrawList();
        var rounding = 4f * ImGuiHelpers.GlobalScale;
        drawList.AddRectFilled(box.Min, box.Max, ImGui.GetColorU32(ImGuiCol.PopupBg), rounding);
        drawList.AddRect(box.Min, box.Max, ImGui.GetColorU32(ImGuiCol.Border), rounding);
    }

    public void Header(GlamPreviewPlacedLabel segment)
        => ImGui.GetForegroundDrawList().AddText(
            segment.Position,
            ImGui.GetColorU32(segment.Enabled ? ImGuiCol.Text : ImGuiCol.TextDisabled),
            segment.Text);

    public void Image(ImTextureID handle, Vector2 min, Vector2 max)
        => ImGui.GetForegroundDrawList().AddImage(handle, min, max, Vector2.Zero, Vector2.One, uint.MaxValue);

    public void Note(Vector2 pos, string text)
        => ImGui.GetForegroundDrawList().AddText(pos, ImGui.GetColorU32(ImGuiCol.TextDisabled), text);
}
