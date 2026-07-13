using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;

namespace GoodGlam.Loot;

/// <summary>
/// Polls the game's native loot state independently of the Need/Greed window and also listens for
/// addon lifecycle events for immediate scans and reconciliation. Each rollable item is dispatched
/// to the popularity check without packet capture.
/// </summary>
public sealed class LootWatcher : IDisposable
{
    private const string AddonName = "NeedGreed";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan EmptyBatchGracePeriod = TimeSpan.FromSeconds(1);

    private readonly IItemResolver resolver;
    private readonly GlamPopularityService popularity;
    private readonly Configuration config;
    private readonly ILootReader lootReader;
    private readonly IDropContextProvider dropContextProvider;
    private readonly ITraceLogger<LootWatcher> log;

    // Identity of a single rollable drop, taken from the game's own bookkeeping: which coffer it came
    // from (ChestObjectId) and its slot within that coffer (ChestItemIndex), plus the raw item id as a
    // cheap tiebreaker. This is what lets us dedup across a window close/reopen (same identity → same
    // drop) while still treating a genuinely new drop of the same item (different coffer/slot) as new.
    private readonly record struct DropKey(uint ChestObjectId, uint ChestItemIndex, uint ItemId);

    // Drops already dispatched for popularity this loot session. Keyed by DropKey (not item id) and
    // persisted across window close so reopening unchanged loot doesn't re-dispatch. Reconciled when
    // the window (re)opens or a new observed loot batch begins; sustained emptiness ends the batch.
    private readonly HashSet<DropKey> dispatchedDrops = [];
    private readonly HashSet<DropKey> unresolvedDrops = [];
    private TimeSpan timeSincePoll;
    private TimeSpan continuouslyEmptyFor;
    private bool pollSessionActive;
    private bool wasBoundByDuty;

    public LootWatcher(IItemResolver resolver, GlamPopularityService popularity, Configuration config)
        : this(
            resolver,
            popularity,
            config,
            new GameLootReader(),
            new DropContextProvider(TimeProvider.System, new GameDutyNameProvider()))
    {
    }

    internal LootWatcher(
        IItemResolver resolver,
        GlamPopularityService popularity,
        Configuration config,
        ILootReader lootReader)
        : this(
            resolver,
            popularity,
            config,
            lootReader,
            new DropContextProvider(TimeProvider.System, new GameDutyNameProvider()))
    {
    }

    internal LootWatcher(
        IItemResolver resolver,
        GlamPopularityService popularity,
        Configuration config,
        ILootReader lootReader,
        IDropContextProvider dropContextProvider,
        ITraceLogger<LootWatcher>? log = null)
    {
        this.resolver = resolver;
        this.popularity = popularity;
        this.config = config;
        this.lootReader = lootReader;
        this.dropContextProvider = dropContextProvider;
        this.log = log ?? new TraceLogger<LootWatcher>();

        Services.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, this.OnAddonEvent);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, AddonName, this.OnAddonEvent);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, this.OnAddonClosed);
        Services.Framework.Update += this.OnFrameworkUpdate;
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
            this.ReadAndScan($"{AddonName} {type}", type == AddonEvent.PostSetup, frameworkPoll: false);
        }
        catch (Exception ex)
        {
            this.log.Error("failed to scan the loot roll window.", ex);
        }
    }

    private void OnAddonClosed(AddonEvent type, AddonArgs args)
    {
        // Deliberately does NOT clear the dispatched set: wiping it here is what caused #6, where
        // reopening the window re-logged unchanged loot. Stale entries are instead reconciled the next
        // time the window opens (PruneDeparted on PostSetup), so the set can safely outlive a window.
        this.log.Verbose($"{AddonName} closed — keeping {this.dispatchedDrops.Count} dispatched drop(s) for reopen dedup.");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var boundByDuty = IsBoundByDuty();
        if (this.wasBoundByDuty && !boundByDuty)
            this.EndObservedLootBatch("left duty");
        this.wasBoundByDuty = boundByDuty;

        if (!boundByDuty)
        {
            this.timeSincePoll = TimeSpan.Zero;
            return;
        }

        if (!this.config.Enabled)
        {
            this.timeSincePoll = TimeSpan.Zero;
            return;
        }

        this.timeSincePoll += framework.UpdateDelta;
        if (this.timeSincePoll < PollInterval)
            return;

        var elapsedSinceLastPoll = this.timeSincePoll;
        this.timeSincePoll = TimeSpan.Zero;
        try
        {
            this.ReadAndScan(
                "native loot poll",
                pruneDeparted: false,
                frameworkPoll: true,
                elapsedSinceLastPoll: elapsedSinceLastPoll);
        }
        catch (Exception ex)
        {
            this.log.Error("failed to poll the native loot state.", ex);
        }
    }

    private static bool IsBoundByDuty() =>
        Services.Condition[ConditionFlag.BoundByDuty]
        || Services.Condition[ConditionFlag.BoundByDuty56]
        || Services.Condition[ConditionFlag.BoundByDuty95];

    private void ReadAndScan(
        string source,
        bool pruneDeparted,
        bool frameworkPoll,
        TimeSpan elapsedSinceLastPoll = default)
    {
        var loot = this.lootReader.Read();
        if (frameworkPoll)
        {
            if (loot is null || !HasPopulatedSlots(loot.Value))
            {
                this.ObserveEmptyPoll(elapsedSinceLastPoll);
                return;
            }

            this.continuouslyEmptyFor = TimeSpan.Zero;
            if (!this.pollSessionActive)
            {
                this.pollSessionActive = true;
                pruneDeparted = true;
            }
        }
        else if (loot is not null && HasPopulatedSlots(loot.Value))
        {
            this.pollSessionActive = true;
            this.continuouslyEmptyFor = TimeSpan.Zero;
        }

        if (loot is not { } snapshot)
        {
            this.log.Verbose($"{source} but no active loot session; nothing to scan.");
            return;
        }

        // Reconcile only when the window (re)opens (PostSetup), never on an in-window refresh. Pruning
        // on a PostRefresh would let a transiently partial loot read evict a still-valid dispatched drop
        // and then re-dispatch it on the next refresh — a milder recurrence of #6. On open we forget any
        // previously-dispatched drop no longer present, so memory stays bounded and a reused chest
        // identity counts as new; within a window we only ever add newly-appeared drops.
        if (pruneDeparted)
            this.PruneDeparted(snapshot);

        var dispatched = 0;
        var skipped = 0;
        if (!frameworkPoll)
            this.log.Debug($"{source} — scanning {snapshot.Items.Count} slot(s).");
        for (var i = 0; i < snapshot.Items.Count; i++)
        {
            var lootItem = snapshot.Items[i];
            if (lootItem.ItemId == 0 || lootItem.RollState == RollState.Unavailable)
            {
                skipped++;
                continue;
            }

            var key = KeyOf(lootItem);
            if (this.dispatchedDrops.Contains(key) || this.unresolvedDrops.Contains(key))
            {
                if (!frameworkPoll)
                    this.log.Verbose($"item {lootItem.ItemId} already handled this batch; skipping.");
                skipped++;
                continue;
            }

            if (!frameworkPoll)
                this.log.Verbose($"slot {i} ItemId={lootItem.ItemId} RollState={lootItem.RollState}.");

            var drop = this.resolver.Resolve(lootItem.ItemId);
            if (drop is null)
            {
                this.unresolvedDrops.Add(key);
                skipped++;
                continue;
            }

            // Slot-level opt-out (#43): a disabled slot is skipped before the dedup add and before any
            // EC lookup, so nothing is logged and re-enabling the slot mid-session takes effect on the
            // next scan (the drop was never recorded as dispatched).
            if (!this.config.IsSlotEnabled(drop.Slot))
            {
                this.log.Verbose($"{drop.Name} ({drop.ItemId}) [slot={drop.Slot.Key}] slot disabled; skipping.");
                skipped++;
                continue;
            }

            // Dedup on the drop's chest identity, not its item id, so two consecutive drops of the same
            // item (different coffer/slot) are both counted while a reopened window is not.
            this.dispatchedDrops.Add(key);

            this.log.Debug($"dispatching {drop.Name} ({drop.ItemId}) [slot={drop.Slot.Key}] for popularity check.");
            dispatched++;
            _ = this.popularity.ProcessAsync(this.dropContextProvider.Capture(drop));
        }

        if (!frameworkPoll || dispatched > 0)
            this.log.Debug($"{source} scan complete — {dispatched} dispatched, {skipped} skipped of {snapshot.Items.Count} slot(s).");
    }

    private static bool HasPopulatedSlots(in LootSnapshot loot)
    {
        for (var i = 0; i < loot.Items.Count; i++)
        {
            if (loot.Items[i].ItemId != 0)
                return true;
        }

        return false;
    }

    private void ObserveEmptyPoll(TimeSpan elapsedSinceLastPoll)
    {
        if (!this.pollSessionActive)
            return;

        this.continuouslyEmptyFor += elapsedSinceLastPoll;
        if (this.continuouslyEmptyFor < EmptyBatchGracePeriod)
            return;

        this.EndObservedLootBatch("native loot remained empty for one second");
    }

    private void EndObservedLootBatch(string reason)
    {
        var cleared = this.dispatchedDrops.Count + this.unresolvedDrops.Count;
        this.dispatchedDrops.Clear();
        this.unresolvedDrops.Clear();
        this.pollSessionActive = false;
        this.continuouslyEmptyFor = TimeSpan.Zero;
        this.log.Verbose($"observed loot batch ended ({reason}); cleared {cleared} handled drop(s).");
    }

    // Forgets any previously-dispatched drop that is no longer present in the live loot. Called when
    // the window (re)opens or polling observes the first populated snapshot after a session boundary;
    // never called for routine refreshes, where a partial view could otherwise cause redispatch.
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
        pruned += this.unresolvedDrops.RemoveWhere(key => !present.Contains(key));
        if (pruned > 0)
            this.log.Verbose($"pruned {pruned} handled drop(s) no longer present since the window last opened.");
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

        if (!this.config.IsSlotEnabled(drop.Slot))
        {
            this.log.Information(
                $"check: {drop.Name} slot '{drop.Slot.Key}' is disabled in settings; skipping — nothing logged.");
            return;
        }

        _ = this.ReportSimulatedDropAsync(this.dropContextProvider.Capture(drop));
    }

    private async Task ReportSimulatedDropAsync(DropOccurrence drop)
    {
        var popularity = await this.popularity.ProcessAsync(drop).ConfigureAwait(false);
        var threshold = this.config.EffectiveThreshold(drop.Slot);
        var passed = popularity.TopLoves >= threshold;
        this.log.Information(
            $"check: {drop.Name} -> topLoves={popularity.TopLoves}, " +
            $"glam={popularity.TopGlamUrl ?? "(none)"}, threshold={threshold} => " +
            $"{(passed ? "POPULAR — logged to history (logo glow raised)" : "below threshold — not logged")}");
    }

    /// <summary>
    /// Debug helper (<c>/goodglam reset</c>): clears the handled-drop sets so the same, still-open loot
    /// is resolved and dispatched through the pipeline again on the next scan. Intended for testing
    /// the detection/notify path repeatedly without needing a fresh coffer.
    /// </summary>
    public void ResetDispatchedDrops()
    {
        var count = this.dispatchedDrops.Count + this.unresolvedDrops.Count;
        this.dispatchedDrops.Clear();
        this.unresolvedDrops.Clear();
        this.log.Information(
            $"reset: cleared {count} handled drop(s); the next loot scan will re-evaluate all rollable items.");
    }

    public void Dispose()
    {
        Services.Framework.Update -= this.OnFrameworkUpdate;
        Services.AddonLifecycle.UnregisterListener(this.OnAddonEvent);
        Services.AddonLifecycle.UnregisterListener(this.OnAddonClosed);
    }
}
