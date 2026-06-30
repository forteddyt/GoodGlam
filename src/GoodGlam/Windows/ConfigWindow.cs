using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using GoodGlam.Glam;

namespace GoodGlam.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly EcFilterCatalog filterCatalog;
    private readonly Action openHistory;
    private readonly Action<bool> setLogoVisible;

    public ConfigWindow(Configuration config, EcFilterCatalog filterCatalog, Action openHistory, Action<bool> setLogoVisible)
        : base("GoodGlam Settings###GoodGlamConfig")
    {
        this.config = config;
        this.filterCatalog = filterCatalog;
        this.openHistory = openHistory;
        this.setLogoVisible = setLogoVisible;
        this.Size = new Vector2(460, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.Button("Open history"))
            this.openHistory();
        Help("Opens the browsable history of popular drops (persists across sessions).");

        var showLogo = this.config.ShowLogo;
        if (ImGui.Checkbox("Show floating logo button", ref showLogo))
            this.setLogoVisible(showLogo);
        Help("Shows a small draggable GoodGlam logo in-game; click it to open the history window.");

        ImGui.Separator();

        var enabled = this.config.Enabled;
        if (ImGui.Checkbox("Enable drop notifications", ref enabled))
        {
            this.config.Enabled = enabled;
            this.config.Save();
        }

        Help("Master switch. When off, GoodGlam never checks dropped items or logs popular drops.");

        ImGui.Separator();
        ImGui.TextWrapped(
            "Notify when a dropped item is used in a glamour with at least this many loves on Eorzea Collection:");

        var threshold = this.config.LovesThreshold;
        if (ImGui.InputInt("Loves threshold", ref threshold, 10, 100, default))
        {
            this.config.LovesThreshold = Math.Max(0, threshold);
            this.config.Save();
        }

        Help("Minimum 'loves' a glamour must have for a drop to count as popular. Higher = pickier.");

        var ttl = this.config.CacheTtlHours;
        if (ImGui.InputInt("Cache lifetime (hours)", ref ttl, 1, 6, default))
        {
            this.config.CacheTtlHours = Math.Clamp(ttl, 1, 72);
            this.config.Save();
        }

        Help("How long a popularity result is reused before re-checking Eorzea Collection. " +
            "Longer = fewer requests, but slower to reflect new glamours. Clamped to 1-72 hours.");

        ImGui.Separator();
        this.DrawFilters();

        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button("Restore Defaults"))
            this.RestoreDefaults();
        Help("Reverts every GoodGlam setting (notifications, threshold, cache, and all filters) to defaults.");
    }

    /// <summary>Draws Dalamud's standard info "(?)" icon on the same line, with a hover tooltip.</summary>
    private static void Help(string text)
    {
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(text);
    }

    private void RestoreDefaults()
    {
        var defaults = new Configuration();
        this.config.Enabled = defaults.Enabled;
        this.config.LovesThreshold = defaults.LovesThreshold;
        this.config.CacheTtlHours = defaults.CacheTtlHours;
        this.config.Filters = defaults.Filters;
        this.config.Save();
    }

    private void DrawFilters()
    {
        ImGui.TextWrapped(
            "Filters mirror Eorzea Collection and apply to every popularity check. With everything " +
            "left at its default, lookups behave exactly as if unfiltered.");

        var filters = this.config.Filters;

        this.DrawCombo("Gender", this.filterCatalog.Genders, filters.Gender, v => filters.Gender = v,
            "Only count glamours shown for this character gender.");
        this.DrawRacePicker(filters);
        this.DrawCombo("Intended for", this.filterCatalog.Jobs, filters.Job, v => filters.Job = v,
            "Only count glamours tagged for this role or job.");
        this.DrawCombo("Date submitted", this.filterCatalog.DatePeriods, filters.DatePeriod, v => filters.DatePeriod = v,
            "Only count glamours submitted within this time window.");
        this.DrawLevelRange(filters);

        this.DrawCombo("Classification", this.filterCatalog.Classifications, filters.Classification, v => filters.Classification = v,
            "EC overall vibe tag (e.g. Cute, Cool, Sexy).");
        this.DrawCombo("Style", this.filterCatalog.Styles, filters.Style, v => filters.Style = v,
            "EC style tag (e.g. Casual, Fantasy, Modern).");
        this.DrawCombo("Theme", this.filterCatalog.Themes, filters.Theme, v => filters.Theme = v,
            "EC theme tag (e.g. Swimwear, Battle Gear, Royalty).");
        this.DrawCombo("Color", this.filterCatalog.Colors, filters.Color, v => filters.Color = v,
            "EC dominant color tag.");

        var noMog = filters.ExcludeMogstation;
        if (ImGui.Checkbox("Exclude Mog Station", ref noMog))
        {
            filters.ExcludeMogstation = noMog;
            this.config.Save();
        }

        Help("Ignore glamours that use Mog Station (cash-shop) items.");

        var noSeasonal = filters.ExcludeSeasonal;
        if (ImGui.Checkbox("Exclude seasonal", ref noSeasonal))
        {
            filters.ExcludeSeasonal = noSeasonal;
            this.config.Save();
        }

        Help("Ignore glamours that use limited seasonal event gear.");

        ImGui.Spacing();
        if (ImGui.Button("Reset filters"))
        {
            this.config.Filters = new PopularityFilters();
            this.config.Save();
        }

        Help("Clears only the filters above; keeps notifications, threshold, and cache settings.");
    }

    private void DrawCombo(string label, IReadOnlyList<EcFilterOption> options, string current, Action<string> set, string help)
    {
        var preview = options.FirstOrDefault(o => o.Value == current)?.Label ?? options[0].Label;
        if (ImGui.BeginCombo(label, preview))
        {
            foreach (var option in options)
            {
                if (ImGui.Selectable(option.Label, option.Value == current) && option.Value != current)
                {
                    set(option.Value);
                    this.config.Save();
                }
            }

            ImGui.EndCombo();
        }

        Help(help);
    }

    private void DrawRacePicker(PopularityFilters filters)
    {
        var preview = filters.Races.Count == 0
            ? "All races"
            : string.Join(", ", this.filterCatalog.Races.Where(r => filters.Races.Contains(r.Value)).Select(r => r.Label));

        if (ImGui.BeginCombo("Race", preview))
        {
            foreach (var race in this.filterCatalog.Races)
            {
                var selected = filters.Races.Contains(race.Value);
                if (!ImGui.Selectable(race.Label, selected, ImGuiSelectableFlags.DontClosePopups))
                    continue;

                if (selected)
                    filters.Races.Remove(race.Value);
                else
                    filters.Races.Add(race.Value);
                this.config.Save();
            }

            ImGui.EndCombo();
        }

        Help("Only count glamours for the selected races; pick several or none for all.");
    }

    private void DrawLevelRange(PopularityFilters filters)
    {
        var min = filters.MinLevel;
        var max = filters.MaxLevel;
        if (ImGui.InputInt("Min level to equip", ref min, 1, 5, default))
        {
            filters.MinLevel = Math.Clamp(min, EcFilterOptions.MinLevel, EcFilterOptions.MaxLevel);
            filters.MaxLevel = Math.Max(filters.MaxLevel, filters.MinLevel);
            this.config.Save();
        }

        Help("Lowest item equip level to include. Stays at or below the max.");

        if (ImGui.InputInt("Max level to equip", ref max, 1, 5, default))
        {
            filters.MaxLevel = Math.Clamp(max, EcFilterOptions.MinLevel, EcFilterOptions.MaxLevel);
            filters.MinLevel = Math.Min(filters.MinLevel, filters.MaxLevel);
            this.config.Save();
        }

        Help("Highest item equip level to include. Stays at or above the min.");
    }
}
