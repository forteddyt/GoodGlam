using Dalamud.Configuration;
using GoodGlam.Glam;

namespace GoodGlam;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    /// <summary>Master toggle for drop notifications.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the small floating logo button is shown in-game.</summary>
    public bool ShowLogo { get; set; } = true;

    /// <summary>When true, the floating logo button can't be dragged (position is locked).</summary>
    public bool LockLogo { get; set; } = false;

    /// <summary>
    /// A dropped item counts as "popular" when at least one glamour using it has
    /// this many loves (or more) on Eorzea Collection.
    /// </summary>
    public int LovesThreshold { get; set; } = 100;

    /// <summary>How long (hours) a popularity lookup is cached before being refreshed.</summary>
    public int CacheTtlHours { get; set; } = 12;

    /// <summary>
    /// Global Eorzea Collection filters applied to every popularity lookup. Defaults are inert,
    /// so an unconfigured plugin behaves exactly like the original unfiltered check.
    /// </summary>
    public PopularityFilters Filters { get; set; } = new();

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
