using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using System.Diagnostics.CodeAnalysis;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;

namespace GoodGlam.Windows;

/// <summary>
/// The Filters tab of the unified <see cref="MainWindow"/>: the full Eorzea Collection filter set
/// (Gender, Race, Intended for, Date submitted, Min/Max level, Classification, Style, Theme, Color,
/// Exclude Mog Station, Exclude seasonal) plus <b>Reset filters</b>. Split out of
/// <see cref="SettingsTab"/> so plugin config and the large EC filter set are each single-purpose.
/// </summary>
/// <remarks>
/// Rendering only. Every control's effect (filter mutation, clamping, reset) lives in the pure,
/// unit-tested <see cref="SettingsActions"/>; each widget here is thin wiring that reads an ImGui
/// control and calls one action. That's why this class is excluded from coverage while the behavior
/// behind it is fully tested.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering + thin wiring; the control effects are extracted into the tested SettingsActions, and a live ImGui context can't run in CI.")]
internal sealed class FiltersTab
{
    private readonly Configuration config;
    private readonly EcFilterCatalog filterCatalog;
    private readonly SettingsActions actions;
    private readonly ITraceLogger<FiltersTab> log = new TraceLogger<FiltersTab>();

    internal FiltersTab(Configuration config, EcFilterCatalog filterCatalog, SettingsActions actions)
    {
        this.config = config;
        this.filterCatalog = filterCatalog;
        this.actions = actions;
    }

    internal void Draw()
    {
        ImGui.TextWrapped(
            "Notify when a dropped item is used in a glamour with at least this many loves on Eorzea Collection:");

        var threshold = this.config.LovesThreshold;
        if (ImGui.InputInt("Loves threshold", ref threshold, 10, 100, default))
        {
            this.actions.SetLovesThreshold(threshold);
            this.log.Debug($"filter changed: LovesThreshold = {this.config.LovesThreshold}.");
        }

        Help("Minimum 'loves' a glamour must have for a drop to count as popular. Higher = pickier.");

        ImGui.Separator();
        ImGui.TextWrapped(
            "Filters mirror Eorzea Collection and apply to every popularity check. With everything " +
            "left at its default, lookups behave exactly as if unfiltered.");

        var filters = this.config.Filters;

        this.DrawCombo("Gender", this.filterCatalog.Genders, FilterField.Gender,
            "Only count glamours shown for this character gender.");
        this.DrawRacePicker(filters);
        this.DrawCombo("Intended for", this.filterCatalog.Jobs, FilterField.Job,
            "Only count glamours tagged for this role or job.");
        this.DrawCombo("Date submitted", this.filterCatalog.DatePeriods, FilterField.DatePeriod,
            "Only count glamours submitted within this time window.");
        this.DrawLevelRange(filters);

        this.DrawCombo("Classification", this.filterCatalog.Classifications, FilterField.Classification,
            "EC overall vibe tag (e.g. Cute, Cool, Sexy).");
        this.DrawCombo("Style", this.filterCatalog.Styles, FilterField.Style,
            "EC style tag (e.g. Casual, Fantasy, Modern).");
        this.DrawCombo("Theme", this.filterCatalog.Themes, FilterField.Theme,
            "EC theme tag (e.g. Swimwear, Battle Gear, Royalty).");
        this.DrawCombo("Color", this.filterCatalog.Colors, FilterField.Color,
            "EC dominant color tag.");

        var noMog = filters.ExcludeMogstation;
        if (ImGui.Checkbox("Exclude Mog Station", ref noMog))
        {
            this.log.Debug($"filter changed: ExcludeMogstation = {noMog}.");
            this.actions.SetExcludeMogstation(noMog);
        }

        Help("Ignore glamours that use Mog Station (cash-shop) items.");

        var noSeasonal = filters.ExcludeSeasonal;
        if (ImGui.Checkbox("Exclude seasonal", ref noSeasonal))
        {
            this.log.Debug($"filter changed: ExcludeSeasonal = {noSeasonal}.");
            this.actions.SetExcludeSeasonal(noSeasonal);
        }

        Help("Ignore glamours that use limited seasonal event gear.");

        ImGui.Spacing();
        if (ImGui.Button("Reset filters"))
        {
            this.log.Debug("Reset filters clicked; clearing all filter selections.");
            this.actions.ResetFilters();
        }

        Help("Clears only the Eorzea Collection filters; keeps the loves threshold, notifications, and cache settings.");
    }

    /// <summary>Draws Dalamud's standard info "(?)" icon on the same line, with a hover tooltip.</summary>
    private static void Help(string text)
    {
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(text);
    }

    private void DrawCombo(string label, IReadOnlyList<EcFilterOption> options, FilterField field, string help)
    {
        var current = this.actions.GetFilter(field);
        var preview = options.FirstOrDefault(o => o.Value == current)?.Label ?? options[0].Label;
        if (ImGui.BeginCombo(label, preview))
        {
            foreach (var option in options)
            {
                if (ImGui.Selectable(option.Label, option.Value == current) && option.Value != current)
                {
                    this.log.Debug($"filter changed: {label} = '{option.Label}'.");
                    this.actions.SelectFilter(field, option.Value);
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
                if (ImGui.Selectable(race.Label, selected, ImGuiSelectableFlags.DontClosePopups))
                {
                    this.log.Debug($"filter changed: Race '{race.Label}' {(selected ? "removed" : "added")}.");
                    this.actions.ToggleRace(race.Value);
                }
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
            this.actions.SetMinLevel(min);
            this.log.Debug($"filter changed: level range = {filters.MinLevel}-{filters.MaxLevel}.");
        }

        Help("Lowest item equip level to include. Stays at or below the max.");

        if (ImGui.InputInt("Max level to equip", ref max, 1, 5, default))
        {
            this.actions.SetMaxLevel(max);
            this.log.Debug($"filter changed: level range = {filters.MinLevel}-{filters.MaxLevel}.");
        }

        Help("Highest item equip level to include. Stays at or above the min.");
    }
}
