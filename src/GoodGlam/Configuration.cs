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

    /// <summary>
    /// Where <see cref="Save"/> persists to. Set by the active character's data binding so saves
    /// land in that character's <c>config.json</c>. Left null when no character is active (title
    /// screen), which makes <see cref="Save"/> a deliberate no-op. Not serialized: it is an
    /// internal field, so neither System.Text.Json (properties only) nor Newtonsoft (public
    /// members) picks it up when reading/writing the config file.
    /// </summary>
    internal Action<Configuration>? SaveSink;

    /// <summary>
    /// Persists the current settings for the active character. A no-op while no character is
    /// logged in, so edits made on the title screen are intentionally discarded.
    /// </summary>
    public void Save() => this.SaveSink?.Invoke(this);

    /// <summary>
    /// Copies every persisted field from <paramref name="other"/> into this instance so the single
    /// live configuration object can adopt another character's settings without changing identity
    /// (every window/service keeps its existing reference). The <see cref="SaveSink"/> is owned by
    /// the data binding and intentionally left untouched here.
    /// </summary>
    internal void CopyFrom(Configuration other)
    {
        this.Version = other.Version;
        this.Enabled = other.Enabled;
        this.ShowLogo = other.ShowLogo;
        this.LockLogo = other.LockLogo;
        this.LovesThreshold = other.LovesThreshold;
        this.CacheTtlHours = other.CacheTtlHours;
        this.Filters = other.Filters ?? new PopularityFilters();
    }
}
