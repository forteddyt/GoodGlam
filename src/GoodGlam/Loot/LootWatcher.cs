using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;
using CSLoot = FFXIVClientStructs.FFXIV.Client.Game.UI.Loot;

namespace GoodGlam.Loot;

/// <summary>
/// Hooks the Need/Greed roll window. When it appears (or refreshes), each rollable
/// item is read straight from the game's <see cref="Loot"/> struct and dispatched to
/// the popularity check. No packet capture required.
/// </summary>
public sealed class LootWatcher : IDisposable
{
    private const string AddonName = "NeedGreed";

    private readonly ItemResolver resolver;
    private readonly GlamPopularityService popularity;
    private readonly Configuration config;
    private readonly ITraceLogger<LootWatcher> log;

    // Avoids dispatching the same item twice while a single roll window is open.
    private readonly HashSet<uint> seenThisWindow = [];

    public LootWatcher(
        ItemResolver resolver,
        GlamPopularityService popularity,
        Configuration config,
        ITraceLogger<LootWatcher>? log = null)
    {
        this.resolver = resolver;
        this.popularity = popularity;
        this.config = config;
        this.log = log ?? new TraceLogger<LootWatcher>();

        Services.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, this.OnAddonEvent);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, AddonName, this.OnAddonEvent);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, this.OnAddonClosed);
    }

    private void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        if (!this.config.Enabled)
        {
            this.log.Verbose($"{AddonName} {type} ignored — plugin disabled in settings.");
            return;
        }

        try
        {
            this.ScanLoot(type);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "failed to scan the loot roll window.");
        }
    }

    private void OnAddonClosed(AddonEvent type, AddonArgs args)
    {
        this.log.Verbose($"{AddonName} closed — clearing {this.seenThisWindow.Count} seen item(s).");
        this.seenThisWindow.Clear();
    }

    private unsafe void ScanLoot(AddonEvent type)
    {
        var loot = CSLoot.Instance();
        if (loot == null)
        {
            this.log.Verbose($"{AddonName} {type} but Loot.Instance() is null; nothing to scan.");
            return;
        }

        var items = loot->Items;
        var dispatched = 0;
        var skipped = 0;
        this.log.Debug($"{AddonName} {type} — scanning {items.Length} slot(s).");
        for (var i = 0; i < items.Length; i++)
        {
            var lootItem = items[i];
            if (lootItem.ItemId == 0 || lootItem.RollState == RollState.Unavailable)
            {
                skipped++;
                continue;
            }

            this.log.Verbose($"slot {i} ItemId={lootItem.ItemId} RollState={lootItem.RollState}.");

            var drop = this.resolver.Resolve(lootItem.ItemId);
            if (drop is null)
            {
                skipped++;
                continue;
            }

            if (!this.seenThisWindow.Add(drop.ItemId))
            {
                this.log.Verbose($"{drop.Name} ({drop.ItemId}) already dispatched this window; skipping.");
                skipped++;
                continue;
            }

            this.log.Debug($"dispatching {drop.Name} ({drop.ItemId}) [slot={drop.Slot.Key}] for popularity check.");
            dispatched++;
            _ = this.popularity.ProcessAsync(drop);
        }

        this.log.Debug($"scan complete — {dispatched} dispatched, {skipped} skipped of {items.Length} slot(s).");
    }

    /// <summary>
    /// Debug helper (<c>/goodglam dump</c>): logs every populated entry of the live
    /// <see cref="Loot"/> struct so we can see exactly what the roll window exposes. Trigger
    /// it during a roll window — running old content solo &amp; unsynced is a reliable,
    /// repeatable way to open one on demand.
    /// </summary>
    public unsafe void DumpCurrentLoot()
    {
        var loot = CSLoot.Instance();
        if (loot == null)
        {
            this.log.Information("dump: Loot.Instance() is null (no active loot session).");
            return;
        }

        var items = loot->Items;
        var populated = 0;
        this.log.Information($"dump: SelectedIndex={loot->SelectedIndex}, {items.Length} slots:");
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (it.ItemId == 0)
                continue;

            populated++;
            var drop = this.resolver.Resolve(it.ItemId);
            var resolved = drop is null ? "non-gear/unresolved" : $"{drop.Name} [slot={drop.Slot.Key}]";
            this.log.Information(
                $"  [{i}] ItemId={it.ItemId} Count={it.ItemCount} GlamourItemId={it.GlamourItemId} " +
                $"RollState={it.RollState} RollResult={it.RollResult} RollValue={it.RollValue} " +
                $"LootMode={it.LootMode} Weekly={it.WeeklyLootItem} Time={it.Time:F1}/{it.MaxTime:F1} " +
                $"Chest(ObjId={it.ChestObjectId},Idx={it.ChestItemIndex}) => {resolved}");
        }

        if (populated == 0)
            this.log.Information("dump: no populated loot entries (open this during a roll window).");
    }

    /// <summary>
    /// Debug helper (<c>/goodglam check &lt;itemId&gt;</c>): pushes a single game item ID through
    /// the real resolve -> Eorzea Collection -> notify pipeline, decoupling an end-to-end check
    /// from waiting on a random loot roll. Lower the loves threshold first to log a history entry.
    /// </summary>
    public void SimulateDrop(uint itemId)
    {
        var drop = this.resolver.Resolve(itemId);
        if (drop is null)
        {
            this.log.Information($"check: item {itemId} did not resolve to glamour-relevant gear.");
            return;
        }

        this.log.Information($"check: simulating drop of {drop.Name} ({drop.ItemId}) [slot={drop.Slot.Key}].");
        _ = this.ReportSimulatedDropAsync(drop);
    }

    private async Task ReportSimulatedDropAsync(DropItem drop)
    {
        var popularity = await this.popularity.ProcessAsync(drop).ConfigureAwait(false);
        var passed = popularity.TopLoves >= this.config.LovesThreshold;
        this.log.Information(
            $"check: {drop.Name} -> topLoves={popularity.TopLoves}, " +
            $"glam={popularity.TopGlamUrl ?? "(none)"}, threshold={this.config.LovesThreshold} => " +
            $"{(passed ? "POPULAR — logged to history (logo glow raised)" : "below threshold — not logged")}");
    }

    public void Dispose()
    {
        Services.AddonLifecycle.UnregisterListener(this.OnAddonEvent);
        Services.AddonLifecycle.UnregisterListener(this.OnAddonClosed);
    }
}
