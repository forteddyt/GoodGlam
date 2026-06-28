using Dalamud.Configuration;

namespace GoodGlam;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Master toggle for drop notifications.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// A dropped item counts as "popular" when at least one glamour using it has
    /// this many loves (or more) on Eorzea Collection.
    /// </summary>
    public int LovesThreshold { get; set; } = 100;

    /// <summary>How long (hours) a popularity lookup is cached before being refreshed.</summary>
    public int CacheTtlHours { get; set; } = 12;

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
