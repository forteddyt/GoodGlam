using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GoodGlam.Glam;

namespace GoodGlam.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Configuration config;

    public ConfigWindow(Configuration config)
        : base("GoodGlam Settings###GoodGlamConfig")
    {
        this.config = config;
        this.Size = new Vector2(460, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var enabled = this.config.Enabled;
        if (ImGui.Checkbox("Enable drop notifications", ref enabled))
        {
            this.config.Enabled = enabled;
            this.config.Save();
        }

        ImGui.Separator();
        ImGui.TextWrapped(
            "Notify when a dropped item is used in a glamour with at least this many loves on Eorzea Collection:");

        var threshold = this.config.LovesThreshold;
        if (ImGui.InputInt("Loves threshold", ref threshold, 10, 100, default))
        {
            this.config.LovesThreshold = Math.Max(0, threshold);
            this.config.Save();
        }

        var ttl = this.config.CacheTtlHours;
        if (ImGui.InputInt("Cache lifetime (hours)", ref ttl, 1, 6, default))
        {
            this.config.CacheTtlHours = Math.Clamp(ttl, 1, 72);
            this.config.Save();
        }

        ImGui.Separator();
        this.DrawFilters();

        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button("Restore Defaults"))
            this.RestoreDefaults();
        ImGui.SameLine();
        ImGui.TextDisabled("Reverts all GoodGlam settings");
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

        this.DrawCombo("Gender", EcFilterOptions.Genders, filters.Gender, v => filters.Gender = v);
        this.DrawRacePicker(filters);
        this.DrawCombo("Intended for", EcFilterOptions.Jobs, filters.Job, v => filters.Job = v);
        this.DrawCombo("Date submitted", EcFilterOptions.DatePeriods, filters.DatePeriod, v => filters.DatePeriod = v);
        this.DrawLevelRange(filters);

        this.DrawCombo("Classification", EcFilterOptions.Classifications, filters.Classification, v => filters.Classification = v);
        this.DrawCombo("Style", EcFilterOptions.Styles, filters.Style, v => filters.Style = v);
        this.DrawCombo("Theme", EcFilterOptions.Themes, filters.Theme, v => filters.Theme = v);
        this.DrawCombo("Color", EcFilterOptions.Colors, filters.Color, v => filters.Color = v);

        var noMog = filters.ExcludeMogstation;
        if (ImGui.Checkbox("Exclude Mog Station", ref noMog))
        {
            filters.ExcludeMogstation = noMog;
            this.config.Save();
        }

        var noSeasonal = filters.ExcludeSeasonal;
        if (ImGui.Checkbox("Exclude seasonal", ref noSeasonal))
        {
            filters.ExcludeSeasonal = noSeasonal;
            this.config.Save();
        }

        ImGui.Spacing();
        if (ImGui.Button("Reset filters"))
        {
            this.config.Filters = new PopularityFilters();
            this.config.Save();
        }
    }

    private void DrawCombo(string label, IReadOnlyList<EcFilterOption> options, string current, Action<string> set)
    {
        var preview = options.FirstOrDefault(o => o.Value == current)?.Label ?? options[0].Label;
        if (!ImGui.BeginCombo(label, preview))
            return;

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

    private void DrawRacePicker(PopularityFilters filters)
    {
        var preview = filters.Races.Count == 0
            ? "All races"
            : string.Join(", ", EcFilterOptions.Races.Where(r => filters.Races.Contains(r.Value)).Select(r => r.Label));

        if (!ImGui.BeginCombo("Race", preview))
            return;

        foreach (var race in EcFilterOptions.Races)
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

        if (ImGui.InputInt("Max level to equip", ref max, 1, 5, default))
        {
            filters.MaxLevel = Math.Clamp(max, EcFilterOptions.MinLevel, EcFilterOptions.MaxLevel);
            filters.MinLevel = Math.Min(filters.MinLevel, filters.MaxLevel);
            this.config.Save();
        }
    }
}
