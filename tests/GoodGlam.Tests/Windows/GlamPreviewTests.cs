using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using FakeItEasy;
using FluentAssertions;
using GoodGlam.History;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Guards how the History-tab glam cover preview is placed and, crucially, that it's painted on the
/// ImGui <b>foreground</b> layer so it floats above the GoodGlam window (the regression fixed when it
/// was previously drawn in a plain window that rendered behind the focused main window). The live
/// ImGui submission itself can't run in CI, so these cover the extracted decision seam:
/// <see cref="GlamPreviewLayout"/>, <see cref="GlamPreviewRenderer"/>, and the layer declared by the
/// production <see cref="ForegroundPreviewCanvas"/> / <see cref="HistoryTab"/>.
/// </summary>
public class GlamPreviewTests
{
    public GlamPreviewTests() => TestServices.EnsureLog();

    // ---- Foreground-layer guards (the actual ask) ----

    [Fact]
    public void Production_canvas_draws_on_the_foreground_layer()
        => new ForegroundPreviewCanvas().Layer.Should().Be(PreviewLayer.Foreground);

    [Fact]
    public void History_tab_previews_on_the_foreground_layer()
    {
        var tab = new HistoryTab(new NotificationHistoryStore(string.Empty), new FakeLinkOpener());
        tab.PreviewLayer.Should().Be(PreviewLayer.Foreground,
            "the cover preview must float above the GoodGlam window, not behind it");
    }

    // ---- Placement maths ----

    [Fact]
    public void Layout_anchors_to_the_right_of_the_icon_with_scaled_padding()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(100, 200),
            iconMax: new Vector2(120, 220),
            contentSize: new Vector2(300, 300),
            displaySize: new Vector2(1920, 1080),
            scale: 1f);

        // x = iconMax.X + gap(8); y = iconMin.Y.
        box.Min.Should().Be(new Vector2(128, 200));
        // box = content(300) + padding(6)*2 = 312.
        box.Max.Should().Be(new Vector2(128 + 312, 200 + 312));
        box.ContentMin.Should().Be(new Vector2(128 + 6, 200 + 6));
        box.ContentSize.Should().Be(new Vector2(300, 300));
    }

    [Fact]
    public void Layout_flips_to_the_left_when_the_box_would_overflow_the_display()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(1000, 100),
            iconMax: new Vector2(1020, 120),
            contentSize: new Vector2(300, 300),
            displaySize: new Vector2(1080, 1080), // right side (1020+8+312=1340) overflows 1080
            scale: 1f);

        // Flipped: x = iconMin.X - gap(8) - boxWidth(312).
        box.Min.X.Should().Be(1000 - 8 - 312);
        box.Min.Y.Should().Be(100);
        box.Max.X.Should().Be(1000 - 8); // right edge sits a gap left of the icon
    }

    [Fact]
    public void Layout_scales_gap_and_padding_by_the_ui_factor()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(0, 0),
            iconMax: new Vector2(20, 20),
            contentSize: new Vector2(100, 100),
            displaySize: new Vector2(4000, 4000),
            scale: 2f);

        // gap 8*2=16, padding 6*2=12 each side.
        box.Min.X.Should().Be(20 + 16);
        box.ContentMin.Should().Be(new Vector2(20 + 16 + 12, 0 + 12));
        box.Max.Should().Be(box.Min + new Vector2(100 + 24, 100 + 24));
    }

    [Fact]
    public void Layout_clamps_vertically_so_a_bottom_row_preview_stays_on_screen()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(100, 1000), // near the bottom of a 1080-tall display
            iconMax: new Vector2(120, 1020),
            contentSize: new Vector2(300, 300), // box 312 tall -> 1000+312 = 1312 overflows 1080
            displaySize: new Vector2(1920, 1080),
            scale: 1f);

        box.Max.Y.Should().BeLessThanOrEqualTo(1080);
        box.Min.Y.Should().Be(1080 - 312); // pinned so the bottom edge sits on the display edge
    }

    [Fact]
    public void Layout_clamps_the_flipped_x_to_the_left_edge()
    {
        // Icon hugs the left edge and the display is too narrow to fit the box on either side, so the
        // right-side placement overflows and the flip would go negative; both must pin to x = 0.
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(10, 100),
            iconMax: new Vector2(30, 120),
            contentSize: new Vector2(300, 300),
            displaySize: new Vector2(320, 1080),
            scale: 1f);

        box.Min.X.Should().Be(0);
    }

    [Fact]
    public void Layout_pins_to_origin_when_the_box_is_larger_than_the_display()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(50, 50),
            iconMax: new Vector2(70, 70),
            contentSize: new Vector2(500, 500),
            displaySize: new Vector2(200, 200), // box far exceeds the display on both axes
            scale: 1f);

        box.Min.Should().Be(Vector2.Zero);
    }

    // ---- Render flow (background always; image when ready, else note) ----

    [Fact]
    public void Renderer_draws_background_then_image_when_ready()
    {
        var canvas = new RecordingCanvas();
        var box = new GlamPreviewBox(new Vector2(1, 2), new Vector2(3, 4), new Vector2(5, 6), new Vector2(7, 8));
        var image = new GlamImage(GlamImageState.Ready, A.Fake<IDalamudTextureWrap>());

        GlamPreviewRenderer.Render(canvas, box, image, "unused");

        canvas.Calls.Should().Equal("Background", "Image");
        canvas.LastImageMin.Should().Be(box.ContentMin);
        canvas.LastImageMax.Should().Be(box.ContentMin + box.ContentSize);
    }

    [Fact]
    public void Renderer_draws_background_then_note_when_loading()
        => this.AssertNoteRendered(GlamImageState.Loading);

    [Fact]
    public void Renderer_draws_background_then_note_when_failed()
        => this.AssertNoteRendered(GlamImageState.Failed);

    private void AssertNoteRendered(GlamImageState state)
    {
        var canvas = new RecordingCanvas();
        var box = new GlamPreviewBox(Vector2.Zero, Vector2.One, new Vector2(5, 6), Vector2.One);

        GlamPreviewRenderer.Render(canvas, box, new GlamImage(state, null), "the note");

        canvas.Calls.Should().Equal("Background", "Note");
        canvas.LastNotePos.Should().Be(box.ContentMin);
        canvas.LastNoteText.Should().Be("the note");
    }

    [Fact]
    public void Renderer_falls_back_to_note_when_ready_but_texture_is_missing()
    {
        var canvas = new RecordingCanvas();
        var box = new GlamPreviewBox(Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.One);

        // Defensive: a Ready state with a null texture must not attempt to draw an image.
        GlamPreviewRenderer.Render(canvas, box, new GlamImage(GlamImageState.Ready, null), "note");

        canvas.Calls.Should().Equal("Background", "Note");
    }

    private sealed class RecordingCanvas : IGlamPreviewCanvas
    {
        public List<string> Calls { get; } = [];

        public Vector2 LastImageMin { get; private set; }

        public Vector2 LastImageMax { get; private set; }

        public Vector2 LastNotePos { get; private set; }

        public string? LastNoteText { get; private set; }

        public PreviewLayer Layer => PreviewLayer.Foreground;

        public void Background(GlamPreviewBox box) => this.Calls.Add("Background");

        public void Image(ImTextureID handle, Vector2 min, Vector2 max)
        {
            this.Calls.Add("Image");
            this.LastImageMin = min;
            this.LastImageMax = max;
        }

        public void Note(Vector2 pos, string text)
        {
            this.Calls.Add("Note");
            this.LastNotePos = pos;
            this.LastNoteText = text;
        }
    }
}
