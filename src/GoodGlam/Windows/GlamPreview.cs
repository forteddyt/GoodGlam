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

/// <summary>A preview label before layout assigns it a position.</summary>
internal readonly record struct GlamPreviewLabel(string Text, bool Enabled);

/// <summary>Builds the centered header label for the current selected glamour rank.</summary>
internal static class GlamPreviewHeader
{
    internal static GlamPreviewLabel Create(int selectedIndex, int glamCount)
    {
        var hasSelection = glamCount > 0;
        var clampedIndex = hasSelection ? Math.Clamp(selectedIndex, 0, glamCount - 1) : 0;
        var rank = hasSelection ? clampedIndex + 1 : 0;
        return new GlamPreviewLabel($"Rank #{rank}", true);
    }
}

/// <summary>The centered navigation guidance shown directly below the rank header.</summary>
internal static class GlamPreviewNavigation
{
    internal const string Text = "Left/Right Click to Navigate";

    /// <summary>
    /// Fraction of the body font the hint is drawn at. It's a secondary hint, so it reads smaller than
    /// the rank header and the cover; the layout measures and the canvas renders at this same scale.
    /// </summary>
    internal const float FontScale = 0.8f;

    /// <summary>Scales a full-size text measurement to the smaller font the hint is rendered at.</summary>
    internal static Vector2 ScaleMeasurement(Vector2 fullSize) => fullSize * FontScale;
}

/// <summary>Measured navigation/header/body sizes for a preview frame.</summary>
internal readonly record struct GlamPreviewMeasurements(
    Vector2 BodySize,
    Vector2 NavigationSize,
    Vector2 RankLabelSize);

/// <summary>A laid-out text label with its on-screen position and enabled/disabled state.</summary>
internal readonly record struct GlamPreviewPlacedLabel(string Text, Vector2 Position, bool Enabled);

/// <summary>
/// The resolved on-screen rectangle for a glam cover preview: the outer box (background/border), the
/// rank header at the top with the navigation guidance directly beneath it, followed by the
/// image/note body. Pure geometry, so it's unit-testable without a live ImGui context.
/// </summary>
internal readonly record struct GlamPreviewBox(
    Vector2 Min,
    Vector2 Max,
    GlamPreviewPlacedLabel Navigation,
    GlamPreviewPlacedLabel RankLabel,
    Vector2 BodyMin,
    Vector2 BodySize);

/// <summary>
/// Pure placement maths for the cover preview: anchors the box below and beside the hovered icon so
/// the rest of its History row stays visible, flipping above or left when the preferred placement
/// would overflow the display. It centers the rank header, navigation guidance, and body within the
/// widest content element.
/// </summary>
internal static class GlamPreviewLayout
{
    internal const float Gap = 8f;
    internal const float VerticalGap = 3f;
    internal const float Padding = 6f;
    internal const float RankNavigationGap = 3f;
    internal const float NavigationBodyGap = 6f;

    internal static GlamPreviewBox Compute(
        Vector2 iconMin,
        Vector2 iconMax,
        GlamPreviewMeasurements measurements,
        GlamPreviewLabel header,
        Vector2 displaySize,
        float scale)
    {
        var gap = Gap * scale;
        var verticalGap = VerticalGap * scale;
        var padding = Padding * scale;
        var navigationHeight = measurements.NavigationSize.Y;
        var rankHeight = measurements.RankLabelSize.Y;
        var rankNavigationGap = rankHeight > 0f && navigationHeight > 0f ? RankNavigationGap * scale : 0f;
        var navigationBodyGap = navigationHeight > 0f && measurements.BodySize.Y > 0f ? NavigationBodyGap * scale : 0f;
        var contentWidth = MathF.Max(
            measurements.BodySize.X,
            MathF.Max(measurements.NavigationSize.X, measurements.RankLabelSize.X));
        var contentHeight = rankHeight + rankNavigationGap + navigationHeight + navigationBodyGap + measurements.BodySize.Y;
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
        var rankPosition = new Vector2(
            contentMin.X + ((contentWidth - measurements.RankLabelSize.X) / 2f),
            contentMin.Y);
        var navigationPosition = new Vector2(
            contentMin.X + ((contentWidth - measurements.NavigationSize.X) / 2f),
            rankPosition.Y + rankHeight + rankNavigationGap);
        var bodyMin = new Vector2(
            contentMin.X + ((contentWidth - measurements.BodySize.X) / 2f),
            navigationPosition.Y + navigationHeight + navigationBodyGap);

        return new GlamPreviewBox(
            min,
            min + boxSize,
            new GlamPreviewPlacedLabel(GlamPreviewNavigation.Text, navigationPosition, true),
            new GlamPreviewPlacedLabel(header.Text, rankPosition, header.Enabled),
            bodyMin,
            measurements.BodySize);

        static float Clamp(float value, float max) => Math.Max(0f, Math.Min(value, max));
    }
}

/// <summary>
/// The surface the preview paints onto, abstracted so the render flow (which layer, background,
/// rank header, navigation guidance, and image-vs-note body) is testable with a fake, while the
/// real ImGui submission stays in
/// <see cref="ForegroundPreviewCanvas"/>. <see cref="Layer"/> declares which ImGui layer the canvas
/// draws to, so a test can assert the preview is on the foreground.
/// </summary>
internal interface IGlamPreviewCanvas
{
    PreviewLayer Layer { get; }

    void Background(GlamPreviewBox box);

    void Navigation(GlamPreviewPlacedLabel navigation);

    void Header(GlamPreviewPlacedLabel segment);

    void Image(ImTextureID handle, Vector2 min, Vector2 max);

    void Note(Vector2 pos, string text);
}

/// <summary>
/// Chooses what to paint for a preview: background/border, rank header, navigation guidance, then the
/// image/loading body. Pure decision flow over an <see cref="IGlamPreviewCanvas"/>.
/// </summary>
internal static class GlamPreviewRenderer
{
    internal static void Render(IGlamPreviewCanvas canvas, GlamPreviewBox box, GlamImage image, string note)
    {
        canvas.Background(box);
        canvas.Header(box.RankLabel);
        canvas.Navigation(box.Navigation);

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

    public void Navigation(GlamPreviewPlacedLabel navigation)
        => ImGui.GetForegroundDrawList().AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize() * GlamPreviewNavigation.FontScale,
            navigation.Position,
            ImGui.GetColorU32(navigation.Enabled ? ImGuiCol.Text : ImGuiCol.TextDisabled),
            navigation.Text,
            0f);

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
