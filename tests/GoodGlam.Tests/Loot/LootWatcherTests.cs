using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Plugin.Services;
using FakeItEasy;
using FluentAssertions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GoodGlam.Glam;
using GoodGlam.Loot;
using Xunit;

namespace GoodGlam.Tests.Loot;

/// <summary>
/// Exercises <see cref="LootWatcher"/>'s scan/dispatch/dedup logic through its two seams: a faked
/// <see cref="IAddonLifecycle"/> (whose registered handlers are captured and invoked to simulate the
/// Need/Greed window opening) and a <see cref="StubLootReader"/> standing in for the native game
/// struct. Popularity dispatch is observed through a real <see cref="GlamPopularityService"/> backed
/// by the in-memory fakes, so we can count how many drops were pushed through the pipeline.
/// </summary>
public class LootWatcherTests
{
    private readonly IAddonLifecycle addon = A.Fake<IAddonLifecycle>();
    private readonly Dictionary<AddonEvent, IAddonLifecycle.AddonEventDelegate> handlers = new();
    private readonly IItemResolver resolver = A.Fake<IItemResolver>();
    private readonly FakeGlamSource source = new();
    private readonly FakeNotifier notifier = new();
    private readonly Configuration config = new() { Filters = new() };

    public LootWatcherTests()
    {
        TestServices.EnsureLog();
        TestServices.Install("AddonLifecycle", this.addon);
        A.CallTo(() => this.addon.RegisterListener(A<AddonEvent>._, A<string>._, A<IAddonLifecycle.AddonEventDelegate>._))
            .Invokes(call =>
                this.handlers[(AddonEvent)call.Arguments[0]!] = (IAddonLifecycle.AddonEventDelegate)call.Arguments[2]!);

        // FakeItEasy would otherwise synthesize a dummy DropItem for unconfigured ids; default to
        // "not glamour gear" (null) so only ids a test explicitly configures resolve to a drop.
        A.CallTo(() => this.resolver.Resolve(A<uint>._)).Returns((DropItem?)null);
    }

    private static LootEntry Entry(uint itemId, RollState state = RollState.UpToNeed) =>
        new(itemId, 1, 0, state, RollResult.UnAwarded, 0, false, 0f, 0f, 0, 0, LootMode.Normal);

    private static LootSnapshot Snapshot(params LootEntry[] items) => new(0, items);

    private LootWatcher New(StubLootReader reader)
    {
        var popularity = new GlamPopularityService(this.config, this.source, this.notifier);
        return new LootWatcher(this.resolver, popularity, this.config, reader);
    }

    private void Fire(AddonEvent evt) => this.handlers[evt].Invoke(evt, null!);

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

        reader.ReadCalls.Should().Be(0);
        this.notifier.CaptureCalls.Should().Be(0);
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
    public void Closing_the_window_clears_the_seen_set_so_the_next_window_dispatches_again()
    {
        A.CallTo(() => this.resolver.Resolve(3610u)).Returns(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));
        this.New(new StubLootReader(Snapshot(Entry(3610))));

        this.Fire(AddonEvent.PostSetup);
        this.Fire(AddonEvent.PreFinalize); // window closed
        this.Fire(AddonEvent.PostSetup);   // a fresh window

        this.notifier.CaptureCalls.Should().Be(2);
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
    public void Dispose_unregisters_both_handlers()
    {
        var watcher = this.New(new StubLootReader());

        watcher.Dispose();

        A.CallTo(() => this.addon.UnregisterListener(A<IAddonLifecycle.AddonEventDelegate[]>._))
            .MustHaveHappened(2, Times.Exactly);
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
