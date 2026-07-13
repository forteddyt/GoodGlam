using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using FakeItEasy;
using FluentAssertions;
using GoodGlam.History;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

public class GlamPreviewTests
{
    public GlamPreviewTests() => TestServices.EnsureLog();

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

    [Fact]
    public void Header_labels_the_current_rank_and_disables_the_boundary_hint()
    {
        var first = GlamPreviewHeader.Create(selectedIndex: 0, glamCount: 10);
        var last = GlamPreviewHeader.Create(selectedIndex: 9, glamCount: 10);
        var single = GlamPreviewHeader.Create(selectedIndex: 0, glamCount: 1);

        first.LeftHint.Text.Should().Be("↓ left click");
        first.LeftHint.Enabled.Should().BeTrue();
        first.RankLabel.Text.Should().Be("Rank #1");
        first.RightHint.Text.Should().Be("right click ↑");
        first.RightHint.Enabled.Should().BeFalse();

        last.RankLabel.Text.Should().Be("Rank #10");
        last.LeftHint.Enabled.Should().BeFalse();
        last.RightHint.Enabled.Should().BeTrue();

        single.LeftHint.Enabled.Should().BeFalse();
        single.RightHint.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Layout_anchors_below_and_to_the_right_of_the_icon()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(100, 200),
            iconMax: new Vector2(120, 220),
            measurements: BodyOnly(new Vector2(300, 300)),
            header: GlamPreviewHeader.Create(selectedIndex: 0, glamCount: 1),
            displaySize: new Vector2(1920, 1080),
            scale: 1f);

        box.Min.Should().Be(new Vector2(128, 223));
        box.Max.Should().Be(new Vector2(128 + 312, 223 + 312));
        box.BodyMin.Should().Be(new Vector2(128 + 6, 223 + 6));
        box.BodySize.Should().Be(new Vector2(300, 300));
    }

    [Fact]
    public void Layout_flips_to_the_left_when_the_box_would_overflow_the_display()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(1000, 100),
            iconMax: new Vector2(1020, 120),
            measurements: BodyOnly(new Vector2(300, 300)),
            header: GlamPreviewHeader.Create(selectedIndex: 0, glamCount: 1),
            displaySize: new Vector2(1080, 1080),
            scale: 1f);

        box.Min.X.Should().Be(1000 - 8 - 312);
        box.Min.Y.Should().Be(120 + 3);
        box.Max.X.Should().Be(1000 - 8);
    }

    [Fact]
    public void Layout_scales_gap_and_padding_by_the_ui_factor()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(0, 0),
            iconMax: new Vector2(20, 20),
            measurements: BodyOnly(new Vector2(100, 100)),
            header: GlamPreviewHeader.Create(selectedIndex: 0, glamCount: 1),
            displaySize: new Vector2(4000, 4000),
            scale: 2f);

        box.Min.X.Should().Be(20 + 16);
        box.BodyMin.Should().Be(new Vector2(20 + 16 + 12, 20 + 6 + 12));
        box.Max.Should().Be(box.Min + new Vector2(100 + 24, 100 + 24));
    }

    [Fact]
    public void Layout_flips_above_a_bottom_row_so_the_row_stays_visible()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(100, 1000),
            iconMax: new Vector2(120, 1020),
            measurements: BodyOnly(new Vector2(300, 300)),
            header: GlamPreviewHeader.Create(selectedIndex: 0, glamCount: 1),
            displaySize: new Vector2(1920, 1080),
            scale: 1f);

        box.Min.Y.Should().Be(1000 - 3 - 312);
        box.Max.Y.Should().Be(1000 - 3);
    }

    [Fact]
    public void Layout_clamps_the_flipped_x_to_the_left_edge()
    {
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(10, 100),
            iconMax: new Vector2(30, 120),
            measurements: BodyOnly(new Vector2(300, 300)),
            header: GlamPreviewHeader.Create(selectedIndex: 0, glamCount: 1),
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
            measurements: BodyOnly(new Vector2(500, 500)),
            header: GlamPreviewHeader.Create(selectedIndex: 0, glamCount: 1),
            displaySize: new Vector2(200, 200),
            scale: 1f);

        box.Min.Should().Be(Vector2.Zero);
    }

    [Fact]
    public void Layout_reserves_header_width_and_centers_a_short_body_without_overlap()
    {
        var measurements = new GlamPreviewMeasurements(
            BodySize: new Vector2(40, 20),
            LeftHintSize: new Vector2(80, 10),
            RankLabelSize: new Vector2(50, 10),
            RightHintSize: new Vector2(70, 10));
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(100, 200),
            iconMax: new Vector2(120, 220),
            measurements: measurements,
            header: GlamPreviewHeader.Create(selectedIndex: 4, glamCount: 10),
            displaySize: new Vector2(1920, 1080),
            scale: 1f);

        var contentWidth = box.Max.X - box.Min.X - (GlamPreviewLayout.Padding * 2f);

        contentWidth.Should().Be(226);
        box.LeftHint.Position.Should().Be(new Vector2(134, 229));
        box.RankLabel.Position.Should().Be(new Vector2(222, 229));
        box.RightHint.Position.Should().Be(new Vector2(290, 229));
        box.BodyMin.Should().Be(new Vector2(227, 245));
        box.BodySize.Should().Be(new Vector2(40, 20));
        (box.LeftHint.Position.X + measurements.LeftHintSize.X + GlamPreviewLayout.HeaderGap).Should().BeLessThanOrEqualTo(box.RankLabel.Position.X);
        (box.RankLabel.Position.X + measurements.RankLabelSize.X + GlamPreviewLayout.HeaderGap).Should().BeLessThanOrEqualTo(box.RightHint.Position.X);
    }

    [Fact]
    public void Renderer_draws_background_header_then_image_when_ready()
    {
        var canvas = new RecordingCanvas();
        var box = Box();
        var image = new GlamImage(GlamImageState.Ready, A.Fake<IDalamudTextureWrap>());

        GlamPreviewRenderer.Render(canvas, box, image, "unused");

        canvas.Calls.Should().Equal("Background", "Header", "Header", "Header", "Image");
        canvas.HeaderSegments.Select(segment => (segment.Text, segment.Enabled)).Should().Equal(
            ("↓ left click", true),
            ("Rank #1", true),
            ("right click ↑", false));
        canvas.LastImageMin.Should().Be(box.BodyMin);
        canvas.LastImageMax.Should().Be(box.BodyMin + box.BodySize);
    }

    [Fact]
    public void Renderer_draws_background_header_then_note_when_loading()
        => this.AssertNoteRendered(GlamImageState.Loading);

    [Fact]
    public void Renderer_draws_background_header_then_note_when_failed()
        => this.AssertNoteRendered(GlamImageState.Failed);

    private void AssertNoteRendered(GlamImageState state)
    {
        var canvas = new RecordingCanvas();
        var box = Box();

        GlamPreviewRenderer.Render(canvas, box, new GlamImage(state, null), "the note");

        canvas.Calls.Should().Equal("Background", "Header", "Header", "Header", "Note");
        canvas.LastNotePos.Should().Be(box.BodyMin);
        canvas.LastNoteText.Should().Be("the note");
    }

    [Fact]
    public void Renderer_falls_back_to_note_when_ready_but_texture_is_missing()
    {
        var canvas = new RecordingCanvas();

        GlamPreviewRenderer.Render(canvas, Box(), new GlamImage(GlamImageState.Ready, null), "note");

        canvas.Calls.Should().Equal("Background", "Header", "Header", "Header", "Note");
    }

    private static GlamPreviewMeasurements BodyOnly(Vector2 bodySize)
        => new(bodySize, Vector2.Zero, Vector2.Zero, Vector2.Zero);

    private static GlamPreviewBox Box()
        => new(
            Min: new Vector2(1, 2),
            Max: new Vector2(300, 400),
            LeftHint: new GlamPreviewPlacedLabel("↓ left click", new Vector2(10, 20), true),
            RankLabel: new GlamPreviewPlacedLabel("Rank #1", new Vector2(100, 20), true),
            RightHint: new GlamPreviewPlacedLabel("right click ↑", new Vector2(200, 20), false),
            BodyMin: new Vector2(30, 60),
            BodySize: new Vector2(70, 80));

    private sealed class RecordingCanvas : IGlamPreviewCanvas
    {
        public List<string> Calls { get; } = [];

        public List<GlamPreviewPlacedLabel> HeaderSegments { get; } = [];

        public Vector2 LastImageMin { get; private set; }

        public Vector2 LastImageMax { get; private set; }

        public Vector2 LastNotePos { get; private set; }

        public string? LastNoteText { get; private set; }

        public PreviewLayer Layer => PreviewLayer.Foreground;

        public void Background(GlamPreviewBox box) => this.Calls.Add("Background");

        public void Header(GlamPreviewPlacedLabel segment)
        {
            this.Calls.Add("Header");
            this.HeaderSegments.Add(segment);
        }

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
