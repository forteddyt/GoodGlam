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
    {
        if (esc.Head > 0) return Head;
        if (esc.Body > 0) return Body;
        if (esc.Gloves > 0) return Hands;
        if (esc.Legs > 0) return Legs;
        if (esc.Feet > 0) return Feet;
        if (esc.MainHand > 0) return Weapon;
        if (esc.OffHand > 0) return Offhand;
        if (esc.Ears > 0) return Earrings;
        if (esc.Neck > 0) return Necklace;
        if (esc.Wrists > 0) return Bracelets;
        if (esc.FingerL > 0 || esc.FingerR > 0) return Ring;
        return null;
    }
}
