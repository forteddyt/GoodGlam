using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
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

/// <summary>
/// The resolved on-screen rectangle for a glam cover preview: the outer box (background/border) and
/// the inner content rect (image or note) inset by padding. Pure geometry, so it's unit-testable
/// without a live ImGui context.
/// </summary>
internal readonly record struct GlamPreviewBox(Vector2 Min, Vector2 Max, Vector2 ContentMin, Vector2 ContentSize);

/// <summary>
/// Pure placement maths for the cover preview: anchors the box beside the hovered icon (preferring
/// the icon's right side, flipping to its left when the box would overflow the display), and insets
/// the content by a scaled padding. No ImGui calls, so the anchoring/flip behaviour is unit-tested.
/// </summary>
internal static class GlamPreviewLayout
{
    /// <summary>Logical gap between the icon and the preview box, and the box's inner padding.</summary>
    internal const float Gap = 8f;
    internal const float Padding = 6f;

    internal static GlamPreviewBox Compute(Vector2 iconMin, Vector2 iconMax, Vector2 contentSize, Vector2 displaySize, float scale)
    {
        var gap = Gap * scale;
        var padding = new Vector2(Padding, Padding) * scale;
        var boxSize = contentSize + (padding * 2f);

        // Prefer the icon's right side; flip to the left when the box would run off the display edge.
        var x = iconMax.X + gap;
        if (x + boxSize.X > displaySize.X)
            x = iconMin.X - gap - boxSize.X;

        var min = new Vector2(x, iconMin.Y);
        return new GlamPreviewBox(min, min + boxSize, min + padding, contentSize);
    }
}

/// <summary>
/// The surface the preview paints onto, abstracted so the render flow (which layer, background +
/// image-vs-note) is testable with a fake, while the real ImGui submission stays in
/// <see cref="ForegroundPreviewCanvas"/>. <see cref="Layer"/> declares which ImGui layer the canvas
/// draws to, so a test can assert the preview is on the foreground.
/// </summary>
internal interface IGlamPreviewCanvas
{
    PreviewLayer Layer { get; }

    void Background(GlamPreviewBox box);

    void Image(ImTextureID handle, Vector2 min, Vector2 max);

    void Note(Vector2 pos, string text);
}

/// <summary>
/// Chooses what to paint for a preview: always a background/border, then either the cover image (when
/// ready) or a short status note (loading / unavailable). Pure decision flow over an
/// <see cref="IGlamPreviewCanvas"/>, so it's unit-tested with a fake canvas.
/// </summary>
internal static class GlamPreviewRenderer
{
    internal static void Render(IGlamPreviewCanvas canvas, GlamPreviewBox box, GlamImage image, string note)
    {
        canvas.Background(box);

        if (image is { State: GlamImageState.Ready, Texture: { } texture })
            canvas.Image(texture.Handle, box.ContentMin, box.ContentMin + box.ContentSize);
        else
            canvas.Note(box.ContentMin, note);
    }
}

/// <summary>
/// Production canvas: paints the preview onto ImGui's <em>foreground</em> draw list so it always sits
/// above the GoodGlam window. Only the ImGui submission lives here (uncovered — no live context in
/// CI); the layer choice it reports is what the tested seam pins down.
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

    public void Image(ImTextureID handle, Vector2 min, Vector2 max)
        => ImGui.GetForegroundDrawList().AddImage(handle, min, max, Vector2.Zero, Vector2.One, uint.MaxValue);

    public void Note(Vector2 pos, string text)
        => ImGui.GetForegroundDrawList().AddText(pos, ImGui.GetColorU32(ImGuiColors.DalamudGrey), text);
}
