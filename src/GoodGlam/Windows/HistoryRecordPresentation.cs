using GoodGlam.Glam;
using GoodGlam.History;
using GoodGlam.Localization;

namespace GoodGlam.Windows;

internal static class HistoryRecordPresentation
{
    public static string PieceLabel(string slotKey) => new GlamSlot(slotKey).Label;

    public static string SelectedRank(PopularDropRecord record)
        => record.RankedGlams.Count == 0
            ? Loc.Strings.History.NoRank
            : string.Format(Loc.Strings.History.SelectedRankFormat, record.ClampedSelectedIndex + 1);

    public static string DroppedAt(DateTimeOffset droppedAt) => droppedAt.ToLocalTime().ToString("g");

    public static string Duty(string? dutyName)
        => string.IsNullOrWhiteSpace(dutyName) ? Loc.Strings.DropDetails.DutyUnknown : dutyName;
}
