using GoodGlam.Glam;

namespace GoodGlam.Windows;

/// <summary>Identifies a single-select Eorzea Collection combo filter, so both reading the current
/// value and writing a new one map through one tested switch instead of an opaque setter delegate.</summary>
internal enum FilterField
{
    Gender,
    Job,
    DatePeriod,
    Classification,
    Style,
    Theme,
    Color,
}

/// <summary>
/// The pure, ImGui-free effects behind every Settings control: apply a new value to the live
/// <see cref="Configuration"/> (clamping and keeping coupled bounds ordered) and persist it. Split
/// out of <see cref="SettingsTab"/> so the settings behavior can be unit-tested without a live ImGui
/// context — the tab's <c>Draw</c> becomes thin wiring that reads a widget and calls one of these.
/// </summary>
internal sealed class SettingsActions
{
    private readonly Configuration config;
    private readonly Action<bool> setLogoVisible;

    internal SettingsActions(Configuration config, Action<bool> setLogoVisible)
    {
        this.config = config;
        this.setLogoVisible = setLogoVisible;
    }

    /// <summary>Shows/hides the floating logo. Persistence is owned by the callback (see Plugin.SetLogoVisible).</summary>
    internal void SetShowLogo(bool visible) => this.setLogoVisible(visible);

    internal void SetEnabled(bool enabled)
    {
        this.config.Enabled = enabled;
        this.config.Save();
    }

    /// <summary>Loves threshold can't go negative (a manual/spinner underflow floors at 0).</summary>
    internal void SetLovesThreshold(int threshold)
    {
        this.config.LovesThreshold = Math.Max(0, threshold);
        this.config.Save();
    }

    /// <summary>Cache lifetime is clamped to 1-72 hours (a non-positive TTL would hammer the network).</summary>
    internal void SetCacheTtlHours(int hours)
    {
        this.config.CacheTtlHours = Math.Clamp(hours, 1, 72);
        this.config.Save();
    }

    /// <summary>Reads a single-select combo filter's current value. Same field mapping as
    /// <see cref="SelectFilter"/>, so the tab's combo wiring is a single tested seam per field.</summary>
    internal string GetFilter(FilterField field) => field switch
    {
        FilterField.Gender => this.config.Filters.Gender,
        FilterField.Job => this.config.Filters.Job,
        FilterField.DatePeriod => this.config.Filters.DatePeriod,
        FilterField.Classification => this.config.Filters.Classification,
        FilterField.Style => this.config.Filters.Style,
        FilterField.Theme => this.config.Filters.Theme,
        FilterField.Color => this.config.Filters.Color,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown filter field."),
    };

    /// <summary>Applies a single-select combo filter's new value and persists it.</summary>
    internal void SelectFilter(FilterField field, string value)
    {
        var filters = this.config.Filters;
        switch (field)
        {
            case FilterField.Gender: filters.Gender = value; break;
            case FilterField.Job: filters.Job = value; break;
            case FilterField.DatePeriod: filters.DatePeriod = value; break;
            case FilterField.Classification: filters.Classification = value; break;
            case FilterField.Style: filters.Style = value; break;
            case FilterField.Theme: filters.Theme = value; break;
            case FilterField.Color: filters.Color = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown filter field.");
        }

        this.config.Save();
    }

    /// <summary>Toggles a race in/out of the multi-select filter.</summary>
    internal void ToggleRace(string race)
    {
        var races = this.config.Filters.Races;
        if (!races.Remove(race))
            races.Add(race);
        this.config.Save();
    }

    internal void SetExcludeMogstation(bool exclude)
    {
        this.config.Filters.ExcludeMogstation = exclude;
        this.config.Save();
    }

    internal void SetExcludeSeasonal(bool exclude)
    {
        this.config.Filters.ExcludeSeasonal = exclude;
        this.config.Save();
    }

    /// <summary>Sets the min equip level, clamped to the EC bounds and never above the current max.</summary>
    internal void SetMinLevel(int level)
    {
        var filters = this.config.Filters;
        filters.MinLevel = Math.Clamp(level, EcFilterOptions.MinLevel, EcFilterOptions.MaxLevel);
        filters.MaxLevel = Math.Max(filters.MaxLevel, filters.MinLevel);
        this.config.Save();
    }

    /// <summary>Sets the max equip level, clamped to the EC bounds and never below the current min.</summary>
    internal void SetMaxLevel(int level)
    {
        var filters = this.config.Filters;
        filters.MaxLevel = Math.Clamp(level, EcFilterOptions.MinLevel, EcFilterOptions.MaxLevel);
        filters.MinLevel = Math.Min(filters.MinLevel, filters.MaxLevel);
        this.config.Save();
    }

    /// <summary>Reverts notifications, threshold, cache, and all filters to defaults (keeps the logo prefs).</summary>
    internal void RestoreDefaults()
    {
        var defaults = new Configuration();
        this.config.Enabled = defaults.Enabled;
        this.config.LovesThreshold = defaults.LovesThreshold;
        this.config.CacheTtlHours = defaults.CacheTtlHours;
        this.config.Filters = defaults.Filters;
        this.config.Save();
    }

    /// <summary>Clears only the EC filters, keeping notifications/threshold/cache untouched.</summary>
    internal void ResetFilters()
    {
        this.config.Filters = new PopularityFilters();
        this.config.Save();
    }
}
