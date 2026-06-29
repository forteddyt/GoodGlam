using Lumina.Excel.Sheets;

namespace GoodGlam.Glam;

/// <summary>
/// An Eorzea Collection equipment slot. <see cref="Key"/> is used both in the
/// item-search endpoint path (<c>/gear/{Key}/search</c>) and to build the
/// glamour listing filter parameter (<c>filter[{Key}Piece]</c>).
/// </summary>
public sealed record GlamSlot(string Key)
{
    public static readonly GlamSlot Head = new("head");
    public static readonly GlamSlot Body = new("body");
    public static readonly GlamSlot Hands = new("hands");
    public static readonly GlamSlot Legs = new("legs");
    public static readonly GlamSlot Feet = new("feet");
    public static readonly GlamSlot Weapon = new("weapon");
    public static readonly GlamSlot Offhand = new("offhand");
    public static readonly GlamSlot Earrings = new("earrings");
    public static readonly GlamSlot Necklace = new("necklace");
    public static readonly GlamSlot Bracelets = new("bracelets");
    public static readonly GlamSlot Ring = new("ring");

    /// <summary>The query-string parameter name used on the glamour listing filter.</summary>
    public string FilterParam => $"{Key}Piece";

    /// <summary>
    /// Resolves a game item's <see cref="EquipSlotCategory"/> to the matching Eorzea
    /// Collection slot, or <c>null</c> when the item is not glamour-relevant gear
    /// (e.g. materia, crafting materials, soul crystals, belts).
    /// </summary>
    public static GlamSlot? FromEquipSlotCategory(EquipSlotCategory esc)
        => FromSlotFlags(esc.Head, esc.Body, esc.Gloves, esc.Legs, esc.Feet,
            esc.MainHand, esc.OffHand, esc.Ears, esc.Neck, esc.Wrists, esc.FingerL, esc.FingerR);

    /// <summary>
    /// Pure mapping from the raw <c>EquipSlotCategory</c> slot flags (positive = wearable in
    /// that slot) to an EC slot, mirroring the in-game lookup. Split out from
    /// <see cref="FromEquipSlotCategory"/> so the priority order can be unit-tested without a
    /// game-backed <c>EquipSlotCategory</c>. Returns <c>null</c> for non-gear.
    /// </summary>
    internal static GlamSlot? FromSlotFlags(
        sbyte head, sbyte body, sbyte gloves, sbyte legs, sbyte feet,
        sbyte mainHand, sbyte offHand, sbyte ears, sbyte neck, sbyte wrists, sbyte fingerL, sbyte fingerR)
    {
        if (head > 0) return Head;
        if (body > 0) return Body;
        if (gloves > 0) return Hands;
        if (legs > 0) return Legs;
        if (feet > 0) return Feet;
        if (mainHand > 0) return Weapon;
        if (offHand > 0) return Offhand;
        if (ears > 0) return Earrings;
        if (neck > 0) return Necklace;
        if (wrists > 0) return Bracelets;
        if (fingerL > 0 || fingerR > 0) return Ring;
        return null;
    }
}
