using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
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

    // Avoids dispatching the same item twice while a single roll window is open.
    private readonly HashSet<uint> seenThisWindow = [];

    public LootWatcher(ItemResolver resolver, GlamPopularityService popularity, Configuration config)
    {
        this.resolver = resolver;
        this.popularity = popularity;
        this.config = config;

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

    private unsafe void ScanLoot()
    {
        var loot = CSLoot.Instance();
        if (loot == null)
            return;

        var items = loot->Items;
        for (var i = 0; i < items.Length; i++)
        {
            var lootItem = items[i];
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

    public void Dispose()
    {
        Services.AddonLifecycle.UnregisterListener(this.OnAddonEvent);
        Services.AddonLifecycle.UnregisterListener(this.OnAddonClosed);
    }
}
