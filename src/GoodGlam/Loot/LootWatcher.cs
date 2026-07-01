using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;

namespace GoodGlam.Loot;

/// <summary>
/// Hooks the Need/Greed roll window. When it appears (or refreshes), each rollable
/// item is read straight from the game's <c>Loot</c> struct and dispatched to
/// the popularity check. No packet capture required.
/// </summary>
public sealed class LootWatcher : IDisposable
{
    private const string AddonName = "NeedGreed";

    private readonly IItemResolver resolver;
    private readonly GlamPopularityService popularity;
    private readonly Configuration config;
    private readonly ILootReader lootReader;
    private readonly ITraceLogger<LootWatcher> log;

    // Identity of a single rollable drop, taken from the game's own bookkeeping: which coffer it came
    // from (ChestObjectId) and its slot within that coffer (ChestItemIndex), plus the raw item id as a
    // cheap tiebreaker. This is what lets us dedup across a window close/reopen (same identity → same
    // drop) while still treating a genuinely new drop of the same item (different coffer/slot) as new.
    private readonly record struct DropKey(uint ChestObjectId, uint ChestItemIndex, uint ItemId);

    // Drops already dispatched for popularity this loot session. Keyed by DropKey (not item id) and
    // persisted across window close so reopening unchanged loot doesn't re-dispatch. Reconciled on
    // every scan against the live loot so departed drops are forgotten (bounding memory and letting a
    // reused chest identity count as new).
    private readonly HashSet<DropKey> dispatchedDrops = [];

    public LootWatcher(IItemResolver resolver, GlamPopularityService popularity, Configuration config)
        : this(resolver, popularity, config, new GameLootReader())
    {
    }

    internal LootWatcher(
        IItemResolver resolver,
        GlamPopularityService popularity,
        Configuration config,
        ILootReader lootReader,
        ITraceLogger<LootWatcher>? log = null)
    {
        this.resolver = resolver;
        this.popularity = popularity;
        this.config = config;
        this.lootReader = lootReader;
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
            this.log.Error("failed to scan the loot roll window.", ex);
        }
    }

    private void OnAddonClosed(AddonEvent type, AddonArgs args)
    {
        // Deliberately does NOT clear the dispatched set: wiping it here is what caused #6, where
        // reopening the window re-logged unchanged loot. The set self-heals via per-scan reconciliation
        // (departed drops are pruned in ScanLoot), so it can safely outlive a single window.
        this.log.Verbose($"{AddonName} closed — keeping {this.dispatchedDrops.Count} dispatched drop(s) for reopen dedup.");
    }

    private void ScanLoot(AddonEvent type)
    {
        if (this.lootReader.Read() is not { } loot)
        {
            this.log.Verbose($"{AddonName} {type} but no active loot session; nothing to scan.");
            return;
        }

        // Reconcile only when the window (re)opens (PostSetup), never on an in-window refresh. Pruning
        // on a PostRefresh would let a transiently partial loot read evict a still-valid dispatched drop
        // and then re-dispatch it on the next refresh — a milder recurrence of #6. On open we forget any
        // previously-dispatched drop no longer present, so memory stays bounded and a reused chest
        // identity counts as new; within a window we only ever add newly-appeared drops.
        if (type == AddonEvent.PostSetup)
            this.PruneDeparted(loot);

        var dispatched = 0;
        var skipped = 0;
        this.log.Debug($"{AddonName} {type} — scanning {loot.Items.Count} slot(s).");
        for (var i = 0; i < loot.Items.Count; i++)
        {
            var lootItem = loot.Items[i];
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

            // Dedup on the drop's chest identity, not its item id, so two consecutive drops of the same
            // item (different coffer/slot) are both counted while a reopened window is not.
            if (!this.dispatchedDrops.Add(KeyOf(lootItem)))
            {
                this.log.Verbose($"{drop.Name} ({drop.ItemId}) already dispatched this session; skipping.");
                skipped++;
                continue;
            }

            this.log.Debug($"dispatching {drop.Name} ({drop.ItemId}) [slot={drop.Slot.Key}] for popularity check.");
            dispatched++;
            _ = this.popularity.ProcessAsync(drop);
        }

        this.log.Debug($"scan complete — {dispatched} dispatched, {skipped} skipped of {loot.Items.Count} slot(s).");
    }

    // Forgets any previously-dispatched drop that is no longer present in the live loot. Called only
    // when the window (re)opens so an in-window refresh reporting a partial view can never evict a
    // still-valid drop. Keeps the dispatched set a subset of the current window's slots.
    private void PruneDeparted(LootSnapshot loot)
    {
        var present = new HashSet<DropKey>();
        for (var i = 0; i < loot.Items.Count; i++)
        {
            var entry = loot.Items[i];
            if (entry.ItemId != 0)
                present.Add(KeyOf(entry));
        }

        var pruned = this.dispatchedDrops.RemoveWhere(key => !present.Contains(key));
        if (pruned > 0)
            this.log.Verbose($"pruned {pruned} dispatched drop(s) no longer present since the window last opened.");
    }

    // Builds a drop's identity from the raw loot entry (raw item id, before HQ normalization) so it
    // stays byte-stable across a window reopen.
    private static DropKey KeyOf(in LootEntry entry) =>
        new(entry.ChestObjectId, entry.ChestItemIndex, entry.ItemId);

    /// <summary>
    /// Debug helper (<c>/goodglam dump</c>): logs every populated entry of the live
    /// <c>Loot</c> struct so we can see exactly what the roll window exposes. Trigger
    /// it during a roll window — running old content solo &amp; unsynced is a reliable,
    /// repeatable way to open one on demand.
    /// </summary>
    public void DumpCurrentLoot()
    {
        if (this.lootReader.Read() is not { } loot)
        {
            this.log.Information("dump: no active loot session.");
            return;
        }

        var populated = 0;
        this.log.Information($"dump: SelectedIndex={loot.SelectedIndex}, {loot.Items.Count} slots:");
        for (var i = 0; i < loot.Items.Count; i++)
        {
            var it = loot.Items[i];
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

    /// <summary>
    /// Debug helper (<c>/goodglam reset</c>): clears the set of already-dispatched drops so the same,
    /// still-open loot is re-dispatched through the pipeline on the next scan. Intended for testing the
    /// detection/notify path repeatedly without needing a fresh coffer.
    /// </summary>
    public void ResetDispatchedDrops()
    {
        var count = this.dispatchedDrops.Count;
        this.dispatchedDrops.Clear();
        this.log.Information(
            $"reset: cleared {count} dispatched drop(s); the next loot scan will re-dispatch all rollable items.");
    }

    public void Dispose()
    {
        Services.AddonLifecycle.UnregisterListener(this.OnAddonEvent);
        Services.AddonLifecycle.UnregisterListener(this.OnAddonClosed);
    }
}
