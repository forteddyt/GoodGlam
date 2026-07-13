using GoodGlam.Glam;

namespace GoodGlam.Windows;

internal static class HistoryRecordPresentation
{
    public static string PieceLabel(string slotKey) => new GlamSlot(slotKey).Label;

    public static string DroppedAt(DateTimeOffset droppedAt) => droppedAt.ToLocalTime().ToString("g");

    public static string Duty(string? dutyName) => string.IsNullOrWhiteSpace(dutyName) ? "Unknown" : dutyName;
}
