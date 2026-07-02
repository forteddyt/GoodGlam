using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// One row of the Gear slots grid: a left-column slot beside an optional right-column slot,
    /// mirroring FFXIV's equipment window. See <see cref="Grid"/>.
    /// </summary>
    public readonly record struct GridRow(GlamSlot Left, GlamSlot? Right);

    /// <summary>
    /// The slots laid out as the in-game equipment grid: a left "gear" column (Main Hand, Head,
    /// Body, Hands, Legs, Feet) beside a right "accessory" column (Off Hand, Ears, Neck, Wrists,
    /// Rings). Main Hand sits alone on the first row, so that row's right cell is <c>null</c>.
    /// Ring 1 / Ring 2 are bundled into the single <see cref="Ring"/> slot, and there is no
    /// facewear slot (it isn't a glamour category on Eorzea Collection).
    /// </summary>
    public static readonly IReadOnlyList<GridRow> Grid =
    [
        new(Weapon, null),
        new(Head, Offhand),
        new(Body, Earrings),
        new(Hands, Necklace),
        new(Legs, Bracelets),
        new(Feet, Ring),
    ];

    /// <summary>
    /// Every glamour slot, in the reading order of the equipment <see cref="Grid"/> (each row's
    /// left cell then its right cell): Main Hand, Head, Off Hand, Body, Ears, Hands, Neck, Legs,
    /// Wrists, Feet, Rings. Derived from <see cref="Grid"/> so the two never drift apart; used
    /// wherever code needs to iterate every slot.
    /// </summary>
    public static readonly IReadOnlyList<GlamSlot> All =
        Grid.SelectMany(row => row.Right is null ? new[] { row.Left } : [row.Left, row.Right]).ToArray();

    /// <summary>The query-string parameter name used on the glamour listing filter.</summary>
    public string FilterParam => $"{Key}Piece";

    /// <summary>
    /// Human-friendly slot name for the UI. Six slots read differently from their EC
    /// <see cref="Key"/> (Main Hand, Off Hand, Ears, Neck, Wrists, Rings); the rest are the
    /// title-cased key. Expression-bodied on purpose so it stays out of the record's value
    /// equality — two slots are still equal iff their <see cref="Key"/> matches.
    /// </summary>
    public string Label => this.Key switch
    {
        "weapon" => "Main Hand",
        "offhand" => "Off Hand",
        "head" => "Head",
        "body" => "Body",
        "hands" => "Hands",
        "legs" => "Legs",
        "feet" => "Feet",
        "earrings" => "Ears",
        "necklace" => "Neck",
        "bracelets" => "Wrists",
        "ring" => "Rings",
        _ => this.Key,
    };

    /// <summary>
    /// Resolves a game item's <see cref="EquipSlotCategory"/> to the matching Eorzea
    /// Collection slot, or <c>null</c> when the item is not glamour-relevant gear
    /// (e.g. materia, crafting materials, soul crystals, belts).
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Thin adapter over the Lumina EquipSlotCategory game-data struct; the priority logic is factored into FromSlotFlags, which is tested.")]
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
