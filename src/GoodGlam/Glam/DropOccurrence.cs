namespace GoodGlam.Glam;

/// <summary>
/// A resolved item paired with the game context captured when the drop was first detected.
/// </summary>
public sealed record DropOccurrence(
    DropItem Item,
    DateTimeOffset DroppedAt,
    string? DutyName)
{
    public uint ItemId => this.Item.ItemId;

    public string Name => this.Item.Name;

    public GlamSlot Slot => this.Item.Slot;
}
