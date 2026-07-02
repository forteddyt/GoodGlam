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

    /// <summary>
    /// When true, each gear slot uses its own loves threshold (see <see cref="Slots"/>) and the
    /// single master <see cref="LovesThreshold"/> is ignored. Default off, so a fresh or older
    /// config keeps the single-threshold behavior.
    /// </summary>
    public bool PerSlotThresholds { get; set; }

    /// <summary>
    /// Per-slot analysis settings keyed by <see cref="Glam.GlamSlot.Key"/>. A slot with no entry is
    /// enabled and uses the master <see cref="LovesThreshold"/>, so a defaulted-empty map is
    /// back-compatible with an older <c>config.json</c> (every slot analysed, exactly like today).
    /// </summary>
    public Dictionary<string, SlotSetting> Slots { get; set; } = new();

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
    /// Whether drops in <paramref name="slot"/> should be analysed. A slot with no explicit entry
    /// defaults to enabled, so an unconfigured plugin analyses every slot exactly like before.
    /// </summary>
    public bool IsSlotEnabled(GlamSlot slot)
        => !this.Slots.TryGetValue(slot.Key, out var setting) || setting.Enabled;

    /// <summary>
    /// The loves threshold to apply to a drop in <paramref name="slot"/>: the slot's own override
    /// when <see cref="PerSlotThresholds"/> is on and one has been set, otherwise the master
    /// <see cref="LovesThreshold"/>. An unedited slot therefore tracks the master value, so flipping
    /// the advanced toggle on changes nothing until a per-slot value is chosen.
    /// </summary>
    public int EffectiveThreshold(GlamSlot slot)
    {
        if (this.PerSlotThresholds &&
            this.Slots.TryGetValue(slot.Key, out var setting) &&
            setting.LovesThreshold is { } threshold)
        {
            return threshold;
        }

        return this.LovesThreshold;
    }

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
        this.PerSlotThresholds = other.PerSlotThresholds;
        this.Slots = other.Slots ?? new();
        this.CacheTtlHours = other.CacheTtlHours;
        this.Filters = other.Filters ?? new PopularityFilters();
    }
}
