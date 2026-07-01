using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
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

    // Avoids dispatching the same item twice while a single roll window is open.
    private readonly HashSet<uint> seenThisWindow = [];

    public LootWatcher(IItemResolver resolver, GlamPopularityService popularity, Configuration config)
        : this(resolver, popularity, config, new GameLootReader())
    {
    }

    internal LootWatcher(IItemResolver resolver, GlamPopularityService popularity, Configuration config, ILootReader lootReader)
    {
        this.resolver = resolver;
        this.popularity = popularity;
        this.config = config;
        this.lootReader = lootReader;

        Services.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, this.OnAddonEvent);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, AddonName, this.OnAddonEvent);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, this.OnAddonClosed);
    }

    private void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        if (!this.config.Enabled)
            return;

        try
        {
            this.ScanLoot();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "GoodGlam: failed to scan the loot roll window.");
        }
    }

    private void OnAddonClosed(AddonEvent type, AddonArgs args) => this.seenThisWindow.Clear();

    private void ScanLoot()
    {
        if (this.lootReader.Read() is not { } loot)
            return;

        foreach (var lootItem in loot.Items)
        {
            if (lootItem.ItemId == 0 || lootItem.RollState == RollState.Unavailable)
                continue;

            var drop = this.resolver.Resolve(lootItem.ItemId);
            if (drop is null)
                continue;

            if (!this.seenThisWindow.Add(drop.ItemId))
                continue;

            _ = this.popularity.ProcessAsync(drop);
        }
    }

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
            Services.Log.Information("GoodGlam[dump]: Loot.Instance() is null (no active loot session).");
            return;
        }

        var populated = 0;
        Services.Log.Information($"GoodGlam[dump]: SelectedIndex={loot.SelectedIndex}, {loot.Items.Count} slots:");
        for (var i = 0; i < loot.Items.Count; i++)
        {
            var it = loot.Items[i];
            if (it.ItemId == 0)
                continue;

            populated++;
            var drop = this.resolver.Resolve(it.ItemId);
            var resolved = drop is null ? "non-gear/unresolved" : $"{drop.Name} [slot={drop.Slot.Key}]";
            Services.Log.Information(
                $"  [{i}] ItemId={it.ItemId} Count={it.ItemCount} GlamourItemId={it.GlamourItemId} " +
                $"RollState={it.RollState} RollResult={it.RollResult} RollValue={it.RollValue} " +
                $"LootMode={it.LootMode} Weekly={it.WeeklyLootItem} Time={it.Time:F1}/{it.MaxTime:F1} " +
                $"Chest(ObjId={it.ChestObjectId},Idx={it.ChestItemIndex}) => {resolved}");
        }

        if (populated == 0)
            Services.Log.Information("GoodGlam[dump]: no populated loot entries (open this during a roll window).");
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
            Services.Log.Information($"GoodGlam[check]: item {itemId} did not resolve to glamour-relevant gear.");
            return;
        }

        Services.Log.Information($"GoodGlam[check]: simulating drop of {drop.Name} ({drop.ItemId}) [slot={drop.Slot.Key}].");
        _ = this.ReportSimulatedDropAsync(drop);
    }

    private async Task ReportSimulatedDropAsync(DropItem drop)
    {
        var popularity = await this.popularity.ProcessAsync(drop).ConfigureAwait(false);
        var passed = popularity.TopLoves >= this.config.LovesThreshold;
        Services.Log.Information(
            $"GoodGlam[check]: {drop.Name} -> topLoves={popularity.TopLoves}, " +
            $"glam={popularity.TopGlamUrl ?? "(none)"}, threshold={this.config.LovesThreshold} => " +
            $"{(passed ? "POPULAR — logged to history (logo glow raised)" : "below threshold — not logged")}");
    }

    public void Dispose()
    {
        Services.AddonLifecycle.UnregisterListener(this.OnAddonEvent);
        Services.AddonLifecycle.UnregisterListener(this.OnAddonClosed);
    }
}
