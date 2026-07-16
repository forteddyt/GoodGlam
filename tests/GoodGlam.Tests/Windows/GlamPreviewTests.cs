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
    public void Navigation_uses_the_concise_label()
        => GlamPreviewNavigation.Text.Should().Be("Navigation: Left/Right Click");

    [Fact]
    public void Header_labels_the_current_rank()
    {
        GlamPreviewHeader.Create(selectedIndex: 0, glamCount: 10).Text.Should().Be("Rank #1");
        GlamPreviewHeader.Create(selectedIndex: 9, glamCount: 10).Text.Should().Be("Rank #10");
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
    public void Layout_reserves_navigation_width_and_places_it_above_rank_and_body()
    {
        var measurements = new GlamPreviewMeasurements(
            BodySize: new Vector2(40, 20),
            NavigationSize: new Vector2(150, 10),
            RankLabelSize: new Vector2(50, 10));
        var box = GlamPreviewLayout.Compute(
            iconMin: new Vector2(100, 200),
            iconMax: new Vector2(120, 220),
            measurements: measurements,
            header: GlamPreviewHeader.Create(selectedIndex: 4, glamCount: 10),
            displaySize: new Vector2(1920, 1080),
            scale: 1f);

        var contentWidth = box.Max.X - box.Min.X - (GlamPreviewLayout.Padding * 2f);

        contentWidth.Should().Be(150);
        box.Navigation.Text.Should().Be("Navigation: Left/Right Click");
        box.Navigation.Position.Should().Be(new Vector2(134, 229));
        box.RankLabel.Text.Should().Be("Rank #5");
        box.RankLabel.Position.Should().Be(new Vector2(184, 242));
        box.BodyMin.Should().Be(new Vector2(189, 258));
        box.BodySize.Should().Be(new Vector2(40, 20));
    }

    [Fact]
    public void Renderer_draws_background_navigation_header_then_image_when_ready()
    {
        var canvas = new RecordingCanvas();
        var box = Box();
        var image = new GlamImage(GlamImageState.Ready, A.Fake<IDalamudTextureWrap>());

        GlamPreviewRenderer.Render(canvas, box, image, "unused");

        canvas.Calls.Should().Equal("Background", "Navigation", "Header", "Image");
        canvas.LastNavigation.Should().Be(box.Navigation);
        canvas.LastHeader.Should().Be(box.RankLabel);
        canvas.LastImageMin.Should().Be(box.BodyMin);
        canvas.LastImageMax.Should().Be(box.BodyMin + box.BodySize);
    }

    [Fact]
    public void Renderer_draws_background_navigation_header_then_note_when_loading()
        => this.AssertNoteRendered(GlamImageState.Loading);

    [Fact]
    public void Renderer_draws_background_navigation_header_then_note_when_failed()
        => this.AssertNoteRendered(GlamImageState.Failed);

    private void AssertNoteRendered(GlamImageState state)
    {
        var canvas = new RecordingCanvas();
        var box = Box();

        GlamPreviewRenderer.Render(canvas, box, new GlamImage(state, null), "the note");

        canvas.Calls.Should().Equal("Background", "Navigation", "Header", "Note");
        canvas.LastNavigation.Should().Be(box.Navigation);
        canvas.LastHeader.Should().Be(box.RankLabel);
        canvas.LastNotePos.Should().Be(box.BodyMin);
        canvas.LastNoteText.Should().Be("the note");
    }

    [Fact]
    public void Renderer_falls_back_to_note_when_ready_but_texture_is_missing()
    {
        var canvas = new RecordingCanvas();

        GlamPreviewRenderer.Render(canvas, Box(), new GlamImage(GlamImageState.Ready, null), "note");

        canvas.Calls.Should().Equal("Background", "Navigation", "Header", "Note");
    }

    private static GlamPreviewMeasurements BodyOnly(Vector2 bodySize)
        => new(bodySize, Vector2.Zero, Vector2.Zero);

    private static GlamPreviewBox Box()
        => new(
            Min: new Vector2(1, 2),
            Max: new Vector2(300, 400),
            Navigation: new GlamPreviewPlacedLabel(
                "Navigation: Left/Right Click",
                new Vector2(20, 10),
                true),
            RankLabel: new GlamPreviewPlacedLabel("Rank #1", new Vector2(100, 20), true),
            BodyMin: new Vector2(30, 60),
            BodySize: new Vector2(70, 80));

    private sealed class RecordingCanvas : IGlamPreviewCanvas
    {
        public List<string> Calls { get; } = [];

        public GlamPreviewPlacedLabel LastNavigation { get; private set; }

        public GlamPreviewPlacedLabel LastHeader { get; private set; }

        public Vector2 LastImageMin { get; private set; }

        public Vector2 LastImageMax { get; private set; }

        public Vector2 LastNotePos { get; private set; }

        public string? LastNoteText { get; private set; }

        public PreviewLayer Layer => PreviewLayer.Foreground;

        public void Background(GlamPreviewBox box) => this.Calls.Add("Background");

        public void Header(GlamPreviewPlacedLabel header)
        {
            this.Calls.Add("Header");
            this.LastHeader = header;
        }

        public void Navigation(GlamPreviewPlacedLabel navigation)
        {
            this.Calls.Add("Navigation");
            this.LastNavigation = navigation;
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
