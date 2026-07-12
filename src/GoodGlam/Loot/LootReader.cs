using System.Diagnostics.CodeAnalysis;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using CSLoot = FFXIVClientStructs.FFXIV.Client.Game.UI.Loot;

namespace GoodGlam.Loot;

/// <summary>
/// A single rollable entry, flattened out of the game's native <c>LootItem</c> struct into plain
/// managed data. Keeping the fields here (rather than reading the struct inline) is what lets the
/// scan/dump logic in <see cref="LootWatcher"/> be unit-tested without the game running.
/// </summary>
internal readonly record struct LootEntry(
    uint ItemId,
    ushort ItemCount,
    uint GlamourItemId,
    RollState RollState,
    RollResult RollResult,
    byte RollValue,
    bool WeeklyLootItem,
    float Time,
    float MaxTime,
    uint ChestObjectId,
    uint ChestItemIndex,
    LootMode LootMode);

/// <summary>A point-in-time copy of the game's native rollable loot state.</summary>
internal readonly record struct LootSnapshot(int SelectedIndex, IReadOnlyList<LootEntry> Items);

/// <summary>
/// Reads the live native loot state. The one seam over the native game struct: production
/// reads <see cref="CSLoot"/> through a raw pointer (see <see cref="GameLootReader"/>), while tests
/// feed a scripted <see cref="LootSnapshot"/>. Returns <c>null</c> when there is no active session.
/// </summary>
internal interface ILootReader
{
    LootSnapshot? Read();
}

/// <summary>
/// Production <see cref="ILootReader"/>: reads the game's <see cref="CSLoot"/> struct via a raw
/// pointer and copies each entry into a managed <see cref="LootEntry"/>. This is the irreducible
/// native boundary — it dereferences game memory and cannot run outside the game, so it is excluded
/// from coverage; all the logic that consumes its output lives in <see cref="LootWatcher"/> and is
/// tested against a fake reader.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Reads native game memory via a raw pointer; cannot run outside the game.")]
internal sealed unsafe class GameLootReader : ILootReader
{
    public LootSnapshot? Read()
    {
        var loot = CSLoot.Instance();
        if (loot == null)
            return null;

        var items = loot->Items;
        var entries = new List<LootEntry>(items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];

            // This positional copy is the only untested surface of the loot pipeline (the consuming
            // logic in LootWatcher is fake-tested, but this native read is excluded). A transposition
            // of two same-typed fields — e.g. ChestObjectId/ChestItemIndex (uint) or Time/MaxTime
            // (float) — would compile and pass every test, so verify the order in-game via
            // `/goodglam dump` whenever this mapping or the native struct changes.
            entries.Add(new LootEntry(
                it.ItemId, it.ItemCount, it.GlamourItemId, it.RollState, it.RollResult,
                it.RollValue, it.WeeklyLootItem, it.Time, it.MaxTime,
                it.ChestObjectId, it.ChestItemIndex, it.LootMode));
        }

        return new LootSnapshot(loot->SelectedIndex, entries);
    }
}
