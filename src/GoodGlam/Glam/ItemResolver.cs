using System.Diagnostics.CodeAnalysis;
using GoodGlam.Diagnostics;
using Lumina.Excel.Sheets;

namespace GoodGlam.Glam;

/// <summary>A rollable item resolved to the data we need for an Eorzea Collection lookup.</summary>
public sealed record DropItem(uint ItemId, string Name, GlamSlot Slot);

/// <summary>
/// Resolves a game item ID (as reported by the loot roll window) into the name + Eorzea
/// Collection slot needed for a lookup, or <c>null</c> when it isn't glamour-relevant gear.
/// Split out as an interface so <see cref="Loot.LootWatcher"/> can be tested against a fake
/// resolver without the game's Lumina data sheets.
/// </summary>
public interface IItemResolver
{
    DropItem? Resolve(uint itemId);
}

/// <summary>
/// Resolves game item IDs (as reported by the loot roll window) into a name and
/// Eorzea Collection slot using the game's own data sheets via Lumina.
/// </summary>
public sealed class ItemResolver : IItemResolver
{
    private readonly ITraceLogger<ItemResolver> log;

    public ItemResolver(ITraceLogger<ItemResolver>? log = null)
        => this.log = log ?? new TraceLogger<ItemResolver>();

    /// <summary>
    /// Returns a <see cref="DropItem"/> for a glamour-relevant gear piece, or
    /// <c>null</c> when the item does not exist or is not equippable glamour gear.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Resolves via Lumina's live game data sheets (Item/EquipSlotCategory); can't run outside the game. The pure HQ math is factored into NormalizeItemId, which is tested.")]
    public DropItem? Resolve(uint itemId)
    {
        // Normalise HQ items (HQ id = base id + 1,000,000); collectables sit even higher.
        var baseId = NormalizeItemId(itemId);

        var sheet = Services.DataManager.GetExcelSheet<Item>();
        if (sheet is null || !sheet.TryGetRow(baseId, out var item))
        {
            this.log.Debug($"item {itemId} (base {baseId}) has no Item sheet row; not gear.");
            return null;
        }

        var name = item.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(name))
        {
            this.log.Debug($"item {itemId} (base {baseId}) has an empty name; skipping.");
            return null;
        }

        var escRef = item.EquipSlotCategory;
        if (!escRef.IsValid)
        {
            this.log.Debug($"'{name}' ({baseId}) has no valid EquipSlotCategory; not equippable gear.");
            return null;
        }

        var slot = GlamSlot.FromEquipSlotCategory(escRef.Value);
        if (slot is null)
        {
            this.log.Debug($"'{name}' ({baseId}) is equippable but maps to no glamour slot (e.g. belt/soul crystal); skipping.");
            return null;
        }

        this.log.Verbose($"item {itemId} -> '{name}' ({baseId}) [slot={slot.Key}].");
        return new DropItem(baseId, name, slot);
    }

    /// <summary>
    /// Maps a loot item id to its base game item id. HQ items report <c>baseId + 1,000,000</c>;
    /// collectables sit above that and are left untouched. Pure so the HQ math can be unit-tested.
    /// </summary>
    internal static uint NormalizeItemId(uint itemId)
        => itemId is >= 1_000_000 and < 2_000_000 ? itemId - 1_000_000 : itemId;
}
