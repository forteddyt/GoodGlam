using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FakeItEasy;
using FluentAssertions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GoodGlam.Glam;
using GoodGlam.Loot;
using Xunit;

namespace GoodGlam.Tests.Loot;

/// <summary>
/// Exercises <see cref="LootWatcher"/>'s scan/dispatch/dedup logic through faked addon and framework
/// events plus a <see cref="StubLootReader"/> standing in for the native game struct. Popularity
/// dispatch is observed through a real <see cref="GlamPopularityService"/> backed by the in-memory
/// fakes, so we can count how many drops were pushed through the pipeline.
/// </summary>
public class LootWatcherTests
{
    private readonly IAddonLifecycle addon = A.Fake<IAddonLifecycle>();
    private readonly Dictionary<AddonEvent, IAddonLifecycle.AddonEventDelegate> handlers = new();
    private readonly ICondition condition = A.Fake<ICondition>();
    private readonly IFramework framework = A.Fake<IFramework>();
    private readonly IItemResolver resolver = A.Fake<IItemResolver>();
    private readonly FakeGlamSource source = new();
    private readonly FakeNotifier notifier = new();
    private readonly Configuration config = new() { Filters = new() };

    public LootWatcherTests()
    {
        TestServices.EnsureLog();
        TestServices.Install("AddonLifecycle", this.addon);
        TestServices.Install("Condition", this.condition);
        TestServices.Install("Framework", this.framework);
        A.CallTo(() => this.addon.RegisterListener(A<AddonEvent>._, A<string>._, A<IAddonLifecycle.AddonEventDelegate>._))
            .Invokes(call =>
                this.handlers[(AddonEvent)call.Arguments[0]!] = (IAddonLifecycle.AddonEventDelegate)call.Arguments[2]!);

        // FakeItEasy would otherwise synthesize a dummy DropItem for unconfigured ids; default to
        // "not glamour gear" (null) so only ids a test explicitly configures resolve to a drop.
        A.CallTo(() => this.resolver.Resolve(A<uint>._)).Returns((DropItem?)null);
    }

    private static LootEntry Entry(
        uint itemId,
        RollState state = RollState.UpToNeed,
        uint chestObjectId = 0,
        uint chestItemIndex = 0) =>
        new(itemId, 1, 0, state, RollResult.UnAwarded, 0, false, 0f, 0f, chestObjectId, chestItemIndex, LootMode.Normal);

    private static LootSnapshot Snapshot(params LootEntry[] items) => new(0, items);

    private LootWatcher New(StubLootReader reader)
    {
        var popularity = new GlamPopularityService(this.config, this.source, this.notifier);
        return new LootWatcher(this.resolver, popularity, this.config, reader);
    }

    private void Fire(AddonEvent evt) => this.handlers[evt].Invoke(evt, null!);

    private void FireFramework(TimeSpan elapsed)
    {
        A.CallTo(() => this.framework.UpdateDelta).Returns(elapsed);
        this.framework.Update += Raise.FreeForm.With(this.framework);
    }

    private void SetDutyBound(bool bound, ConditionFlag flag = ConditionFlag.BoundByDuty)
        => A.CallTo(() => this.condition[flag]).Returns(bound);

    [Fact]
    public void Ctor_registers_the_three_needgreed_listeners()
    {
        this.New(new StubLootReader());

        A.CallTo(() => this.addon.RegisterListener(AddonEvent.PostSetup, "NeedGreed", A<IAddonLifecycle.AddonEventDelegate>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => this.addon.RegisterListener(AddonEvent.PostRefresh, "NeedGreed", A<IAddonLifecycle.AddonEventDelegate>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => this.addon.RegisterListener(AddonEvent.PreFinalize, "NeedGreed", A<IAddonLifecycle.AddonEventDelegate>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Disabled_config_short_circuits_before_reading_loot()
    {
        this.config.Enabled = false;
        var reader = new StubLootReader(Snapshot(Entry(3610)));
        this.New(reader);

        this.Fire(AddonEvent.PostSetup);
        this.SetDutyBound(true);
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        reader.ReadCalls.Should().Be(0);
        this.notifier.CaptureCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(ConditionFlag.BoundByDuty)]
    [InlineData(ConditionFlag.BoundByDuty56)]
    [InlineData(ConditionFlag.BoundByDuty95)]
    public void Framework_poll_dispatches_loot_without_a_needgreed_addon_event(ConditionFlag dutyFlag)
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var reader = new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100)));
        this.New(reader);
        this.SetDutyBound(true, dutyFlag);

        this.FireFramework(TimeSpan.FromMilliseconds(500));

        reader.ReadCalls.Should().Be(1);
        this.notifier.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public void Framework_poll_does_not_read_loot_outside_a_duty()
    {
        var reader = new StubLootReader(Snapshot(Entry(3610)));
        this.New(reader);

        this.FireFramework(TimeSpan.FromSeconds(10));

        reader.ReadCalls.Should().Be(0);
    }

    [Fact]
    public void Framework_poll_is_throttled_to_500_milliseconds()
    {
        var reader = new StubLootReader(Snapshot());
        this.New(reader);
        this.SetDutyBound(true);

        this.FireFramework(TimeSpan.FromMilliseconds(200));
        this.FireFramework(TimeSpan.FromMilliseconds(299));
        reader.ReadCalls.Should().Be(0);

        this.FireFramework(TimeSpan.FromMilliseconds(1));
        reader.ReadCalls.Should().Be(1);
    }

    [Fact]
    public void Less_than_one_second_empty_does_not_redispatch_unchanged_loot()
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var reader = new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100)));
        this.New(reader);
        this.SetDutyBound(true);
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        reader.Snapshot = Snapshot();
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        reader.Snapshot = Snapshot(Entry(3610, chestObjectId: 100));
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        this.notifier.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public void One_second_continuously_empty_ends_the_observed_loot_batch()
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var reader = new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100)));
        this.New(reader);
        this.SetDutyBound(true);
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        reader.Snapshot = Snapshot();
        this.FireFramework(TimeSpan.FromMilliseconds(500));
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        reader.Snapshot = Snapshot(Entry(3610, chestObjectId: 100));
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        this.notifier.CaptureCalls.Should().Be(2);
    }

    [Fact]
    public void Leaving_a_duty_immediately_clears_the_observed_loot_batch()
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var reader = new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100)));
        this.New(reader);
        this.SetDutyBound(true);
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        this.SetDutyBound(false);
        this.FireFramework(TimeSpan.FromMilliseconds(1));

        this.SetDutyBound(true);
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        this.notifier.CaptureCalls.Should().Be(2);
    }

    [Fact]
    public void Addon_event_remains_a_fallback_outside_a_duty()
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        this.New(new StubLootReader(Snapshot(Entry(3610))));

        this.Fire(AddonEvent.PostSetup);

        this.notifier.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public void Dispatches_each_resolved_gear_piece_once()
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        this.New(new StubLootReader(Snapshot(Entry(3610))));

        this.Fire(AddonEvent.PostSetup);

        this.notifier.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public void Skips_empty_slots_and_unavailable_rolls()
    {
        var reader = new StubLootReader(Snapshot(
            Entry(0),
            Entry(3610, RollState.Unavailable)));
        this.New(reader);

        this.Fire(AddonEvent.PostSetup);

        reader.ReadCalls.Should().Be(1);
        A.CallTo(() => this.resolver.Resolve(A<uint>._)).MustNotHaveHappened();
        this.notifier.CaptureCalls.Should().Be(0);
    }

    [Fact]
    public void Skips_items_that_do_not_resolve_to_gear()
    {
        // Un-configured resolver returns null for every id (non-gear).
        this.New(new StubLootReader(Snapshot(Entry(999))));

        this.Fire(AddonEvent.PostSetup);

        this.notifier.CaptureCalls.Should().Be(0);
    }

    [Fact]
    public void Unresolved_drop_is_resolved_once_per_observed_batch()
    {
        var reader = new StubLootReader(Snapshot(Entry(999, chestObjectId: 100)));
        this.New(reader);
        this.SetDutyBound(true);

        this.FireFramework(TimeSpan.FromMilliseconds(500));
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        A.CallTo(() => this.resolver.Resolve(999)).MustHaveHappenedOnceExactly();

        reader.Snapshot = Snapshot();
        this.FireFramework(TimeSpan.FromMilliseconds(500));
        this.FireFramework(TimeSpan.FromMilliseconds(500));
        reader.Snapshot = Snapshot(Entry(999, chestObjectId: 100));
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        A.CallTo(() => this.resolver.Resolve(999)).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public void Skips_drops_in_a_disabled_slot_but_still_dispatches_enabled_ones()
    {
        // #43 core: a drop whose slot is disabled is skipped before any EC lookup (nothing captured),
        // while a drop in an enabled slot is still dispatched.
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        A.CallTo(() => this.resolver.Resolve(3611u)).Returns(new DropItem(3611, "Cavalry Cuisses", GlamSlot.Legs));
        this.config.Slots[GlamSlot.Hands.Key] = new SlotSetting { Enabled = false };
        this.source.Popularity = new GlamPopularity([new GlamResult(150, "u")]); // above the default 100 threshold, so it notifies
        this.New(new StubLootReader(Snapshot(Entry(3610), Entry(3611, chestItemIndex: 1))));

        this.Fire(AddonEvent.PostSetup);

        // Only the Legs drop is dispatched and notified; the disabled Hands drop is skipped.
        this.notifier.CaptureCalls.Should().Be(1);
        this.notifier.LastDrop!.Slot.Should().Be(GlamSlot.Legs);
    }

    [Fact]
    public void Re_enabling_a_slot_mid_session_dispatches_on_the_next_scan()
    {
        // A disabled-slot drop is never recorded as dispatched, so enabling the slot mid-session lets
        // the still-open loot dispatch on the next refresh.
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        this.config.Slots[GlamSlot.Hands.Key] = new SlotSetting { Enabled = false };
        this.New(new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100))));

        this.Fire(AddonEvent.PostSetup); // slot disabled -> skipped
        this.notifier.CaptureCalls.Should().Be(0);

        this.config.Slots[GlamSlot.Hands.Key].Enabled = true;
        this.Fire(AddonEvent.PostRefresh); // now enabled -> dispatched

        this.notifier.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public void SimulateDrop_skips_a_disabled_slot()
    {
        // /goodglam check on an ignored-slot item must not run the pipeline (logs nothing).
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        this.config.Slots[GlamSlot.Hands.Key] = new SlotSetting { Enabled = false };
        var watcher = this.New(new StubLootReader());

        watcher.SimulateDrop(3610);

        this.notifier.CaptureCalls.Should().Be(0);
    }

    [Fact]
    public void Deduplicates_the_same_item_within_one_window()
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        this.New(new StubLootReader(Snapshot(Entry(3610), Entry(3610))));

        // Two entries for the same item in one snapshot, plus a refresh of the same window.
        this.Fire(AddonEvent.PostSetup);
        this.Fire(AddonEvent.PostRefresh);

        this.notifier.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public void Reopening_the_window_with_unchanged_loot_does_not_redispatch()
    {
        // Regression for #6: closing and reopening the roll window while the same loot is still
        // available must not re-log the drop (nor relight the glow). The drop is identified by its
        // chest slot, which is unchanged across the reopen, so it stays deduplicated past close.
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        this.New(new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100))));

        this.Fire(AddonEvent.PostSetup);
        this.Fire(AddonEvent.PreFinalize); // window closed
        this.Fire(AddonEvent.PostSetup);   // reopened with the SAME loot still available

        this.notifier.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public void Consecutive_same_item_drops_from_different_chests_each_dispatch()
    {
        // Two separate drops of the same item come from different coffers (distinct ChestObjectId),
        // so each is a genuinely new drop and must be dispatched — the flip side of the #6 fix.
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var reader = new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100)));
        this.New(reader);

        this.Fire(AddonEvent.PostSetup);   // first coffer
        this.Fire(AddonEvent.PreFinalize);
        reader.Snapshot = Snapshot(Entry(3610, chestObjectId: 200)); // a second coffer drops the same item
        this.Fire(AddonEvent.PostSetup);

        this.notifier.CaptureCalls.Should().Be(2);
    }

    [Fact]
    public void Same_item_in_two_chest_slots_dispatches_each()
    {
        // The same item occupying two slots of one chest (same ChestObjectId, distinct index) is two
        // real drops, so both must dispatch rather than collapsing to a single item-id entry.
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        this.New(new StubLootReader(Snapshot(
            Entry(3610, chestObjectId: 100, chestItemIndex: 0),
            Entry(3610, chestObjectId: 100, chestItemIndex: 1))));

        this.Fire(AddonEvent.PostSetup);

        this.notifier.CaptureCalls.Should().Be(2);
    }

    [Fact]
    public void A_transiently_empty_refresh_does_not_redispatch()
    {
        // Reconciliation happens only on window open, so a PostRefresh that momentarily reports a
        // partial/empty loot view must NOT evict the dedup state and re-log the drop on the next
        // refresh — the intra-window guard against a milder recurrence of #6.
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var reader = new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100)));
        this.New(reader);

        this.Fire(AddonEvent.PostSetup);                            // dispatch #1
        reader.Snapshot = Snapshot();                              // a refresh transiently sees nothing
        this.Fire(AddonEvent.PostRefresh);                         // must not prune
        reader.Snapshot = Snapshot(Entry(3610, chestObjectId: 100)); // the same drop is back
        this.Fire(AddonEvent.PostRefresh);                         // still deduplicated

        this.notifier.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public void A_new_item_appearing_on_refresh_is_dispatched()
    {
        // Looting a second coffer while the window is still open surfaces new items via PostRefresh;
        // they must dispatch even though no fresh PostSetup fired.
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        A.CallTo(() => this.resolver.Resolve(3611u)).Returns(new DropItem(3611, "Cavalry Cuisses", GlamSlot.Legs));
        var reader = new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100)));
        this.New(reader);

        this.Fire(AddonEvent.PostSetup);                           // dispatch 3610
        reader.Snapshot = Snapshot(
            Entry(3610, chestObjectId: 100),
            Entry(3611, chestObjectId: 200));                      // second coffer's item appears
        this.Fire(AddonEvent.PostRefresh);                         // dispatch 3611

        this.notifier.CaptureCalls.Should().Be(2);
    }

    [Fact]
    public void A_stale_drop_is_pruned_on_reopen_so_a_reused_identity_redispatches()
    {
        // On window open, an identity no longer present is forgotten (keeping the set bounded), so a
        // later window that reuses the same chest identity for a genuinely new drop is not suppressed.
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var reader = new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100)));
        this.New(reader);

        this.Fire(AddonEvent.PostSetup);                            // dispatch #1 (chest 100)
        this.Fire(AddonEvent.PreFinalize);
        reader.Snapshot = Snapshot(Entry(3610, chestObjectId: 200)); // a different coffer this window
        this.Fire(AddonEvent.PostSetup);                            // prunes chest 100, dispatch #2 (chest 200)
        this.Fire(AddonEvent.PreFinalize);
        reader.Snapshot = Snapshot(Entry(3610, chestObjectId: 100)); // chest 100 identity reused later
        this.Fire(AddonEvent.PostSetup);                            // chest 100 was pruned → dispatch #3

        this.notifier.CaptureCalls.Should().Be(3);
    }

    [Fact]
    public void ResetDispatchedDrops_lets_the_same_loot_dispatch_again()
    {
        // Dev helper for testing: clearing the dispatched set makes the currently-open, unchanged loot
        // dispatch again on the next scan, without needing a fresh coffer.
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var watcher = this.New(new StubLootReader(Snapshot(Entry(3610, chestObjectId: 100))));

        this.Fire(AddonEvent.PostSetup);   // dispatch #1
        watcher.ResetDispatchedDrops();    // dev reset
        this.Fire(AddonEvent.PostRefresh); // same loot re-dispatches

        this.notifier.CaptureCalls.Should().Be(2);
    }

    [Fact]
    public void ResetDispatchedDrops_rechecks_unresolved_loot()
    {
        var reader = new StubLootReader(Snapshot(Entry(999, chestObjectId: 100)));
        var watcher = this.New(reader);
        this.SetDutyBound(true);

        this.FireFramework(TimeSpan.FromMilliseconds(500));
        watcher.ResetDispatchedDrops();
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        A.CallTo(() => this.resolver.Resolve(999)).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public void Scan_errors_are_swallowed()
    {
        // A resolver that throws must not let the exception escape the addon callback.
        A.CallTo(() => this.resolver.Resolve(A<uint>._)).Throws(new InvalidOperationException("boom"));
        this.New(new StubLootReader(Snapshot(Entry(3610))));

        this.Invoking(t => t.Fire(AddonEvent.PostSetup)).Should().NotThrow();
    }

    [Fact]
    public void Dispose_unregisters_addon_handlers_and_framework_poll()
    {
        var reader = new StubLootReader(Snapshot());
        var watcher = this.New(reader);
        this.SetDutyBound(true);

        watcher.Dispose();
        this.FireFramework(TimeSpan.FromMilliseconds(500));

        A.CallTo(() => this.addon.UnregisterListener(A<IAddonLifecycle.AddonEventDelegate[]>._))
            .MustHaveHappened(2, Times.Exactly);
        reader.ReadCalls.Should().Be(0);
    }

    [Fact]
    public void SimulateDrop_with_unresolved_item_logs_and_does_not_dispatch()
    {
        var watcher = this.New(new StubLootReader());

        watcher.SimulateDrop(999);

        this.notifier.CaptureCalls.Should().Be(0);
    }

    [Fact]
    public void SimulateDrop_pushes_a_resolved_item_through_the_pipeline()
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var watcher = this.New(new StubLootReader());

        watcher.SimulateDrop(3610);

        this.notifier.CaptureCalls.Should().Be(1);
    }

    [Fact]
    public void DumpCurrentLoot_with_no_session_does_not_throw()
    {
        var watcher = this.New(new StubLootReader(snapshot: null));

        watcher.Invoking(w => w.DumpCurrentLoot()).Should().NotThrow();
    }

    [Fact]
    public void DumpCurrentLoot_logs_populated_and_empty_entries()
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        var watcher = this.New(new StubLootReader(Snapshot(Entry(3610), Entry(0), Entry(9999))));

        watcher.Invoking(w => w.DumpCurrentLoot()).Should().NotThrow();
    }

    [Fact]
    public void Scan_with_no_active_session_is_a_noop()
    {
        // The loot reader returns null when there's no live session; ScanLoot must bail immediately.
        var reader = new StubLootReader(snapshot: null);
        this.New(reader);

        this.Fire(AddonEvent.PostSetup);

        reader.ReadCalls.Should().Be(1);
        this.notifier.CaptureCalls.Should().Be(0);
    }

    [Fact]
    public void Public_ctor_wires_the_default_game_loot_reader()
    {
        // The production constructor supplies a real GameLootReader (constructing it is safe; it only
        // touches native memory when Read() is called). Registration still flows through the seam ctor.
        var popularity = new GlamPopularityService(this.config, this.source, this.notifier);
        using var watcher = new LootWatcher(this.resolver, popularity, this.config);

        A.CallTo(() => this.addon.RegisterListener(A<AddonEvent>._, "NeedGreed", A<IAddonLifecycle.AddonEventDelegate>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public void DumpCurrentLoot_reports_when_no_slots_are_populated()
    {
        var watcher = this.New(new StubLootReader(Snapshot(Entry(0), Entry(0))));

        watcher.Invoking(w => w.DumpCurrentLoot()).Should().NotThrow();
    }
}
