using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.History;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

public class HistoryRecordPresentationTests
{
    [Fact]
    public void Columns_follow_the_history_scanning_order()
        => HistoryTab.ColumnOrder.Should().Equal(
            "Piece",
            "Loves",
            "Item",
            "Preview",
            "Rank",
            "Glam",
            "All Glams",
            "Details");

    [Fact]
    public void SelectedRank_uses_the_current_one_based_glamour_rank()
    {
        var record = new PopularDropRecord(
            1,
            "Item",
            "body",
            [
                new GlamResult(300, "https://ec/glamour/1"),
                new GlamResult(200, "https://ec/glamour/2"),
            ],
            DateTimeOffset.UnixEpoch,
            null,
            selectedIndex: 1);

        HistoryRecordPresentation.SelectedRank(record).Should().Be("#2");
    }

    [Theory]
    [InlineData("weapon", "Main Hand")]
    [InlineData("offhand", "Off Hand")]
    [InlineData("body", "Body")]
    [InlineData("earrings", "Ears")]
    [InlineData("bracelets", "Wrists")]
    [InlineData("ring", "Rings")]
    public void PieceLabel_uses_FFXIV_equipment_names(string slotKey, string expected)
        => HistoryRecordPresentation.PieceLabel(slotKey).Should().Be(expected);

    [Fact]
    public void DroppedAt_formats_the_local_detection_time()
    {
        var droppedAt = new DateTimeOffset(2026, 7, 12, 21, 19, 32, TimeSpan.Zero);

        HistoryRecordPresentation.DroppedAt(droppedAt).Should()
            .Be(droppedAt.ToLocalTime().ToString("g"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Duty_falls_back_to_unknown_when_unavailable(string? dutyName)
        => HistoryRecordPresentation.Duty(dutyName).Should().Be("Unknown");

    [Fact]
    public void Duty_preserves_the_captured_name()
        => HistoryRecordPresentation.Duty("The Aurum Vale").Should().Be("The Aurum Vale");
}
