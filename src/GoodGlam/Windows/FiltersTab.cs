using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using System.Diagnostics.CodeAnalysis;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;

namespace GoodGlam.Windows;

/// <summary>
/// The Filters tab of the unified <see cref="MainWindow"/>: the full Eorzea Collection filter set
/// (Gender, Race, Intended for, Date submitted, Min/Max level, Classification, Style, Theme, Color,
/// Mog Station/seasonal exclusions) at the top, then a dedicated <b>Popularity thresholds</b>
/// subsection below it (the master loves threshold and the per-slot <b>Gear slots</b> grid), and a
/// tab-wide <b>Reset filters</b> at the bottom. Split out of <see cref="SettingsTab"/> so plugin
/// config and the large EC filter set are each single-purpose.
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
    internal const string MogStationLabel = "Ignore glam outfits that use any Mog Station (cash shop) items.";

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
        // Eorzea Collection filters come first: they narrow which glamours are considered.
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
        if (ImGui.Checkbox(MogStationLabel, ref noMog))
        {
            this.log.Debug($"filter changed: ExcludeMogstation = {noMog}.");
            this.actions.SetExcludeMogstation(noMog);
        }

        var noSeasonal = filters.ExcludeSeasonal;
        if (ImGui.Checkbox("Exclude seasonal", ref noSeasonal))
        {
            this.log.Debug($"filter changed: ExcludeSeasonal = {noSeasonal}.");
            this.actions.SetExcludeSeasonal(noSeasonal);
        }

        Help("Ignore glamours that use limited seasonal event gear.");

        // Dedicated subsection below the filters: what counts as popular — the loves threshold and
        // the per-slot gear controls.
        ImGui.Separator();
        ImGui.TextUnformatted("Popularity thresholds");
        Help("How many 'loves' a glamour needs for a drop to count as popular, and which gear slots " +
            "are analysed.");
        this.DrawThreshold();
        this.DrawGearSlots();

        // Tab-wide reset lives at the very bottom; it clears every Filters-tab control (EC filters,
        // the loves threshold, and the gear-slot settings) but leaves the Settings-tab toggle alone.
        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button("Reset filters"))
        {
            this.log.Debug("Reset filters clicked; clearing filters, loves threshold, and slot settings.");
            this.actions.ResetFilters();
        }

        Help("Resets the Filters to their default values.");
    }

    /// <summary>
    /// The master loves threshold control. Renders nothing when per-slot thresholds is on (toggled on
    /// the Settings tab), since each slot's threshold is set inline in the Gear slots grid instead.
    /// </summary>
    private void DrawThreshold()
    {
        if (this.config.PerSlotThresholds)
            return;

        ImGui.TextWrapped(
            "Notify when a dropped item is used in a glamour with at least this many loves on Eorzea Collection:");

        var threshold = this.config.LovesThreshold;
        if (ImGui.InputInt("Loves", ref threshold, 10, 100, default))
        {
            this.actions.SetLovesThreshold(threshold);
            this.log.Debug($"filter changed: LovesThreshold = {this.config.LovesThreshold}.");
        }

        Help("Loves are Eorzea Collection users' likes. Lower values find more glamours; higher values " +
            "only flag more widely loved outfits.");
    }

    /// <summary>
    /// The distinct "Gear slots" section, laid out as a two-column grid mirroring FFXIV's equipment
    /// window (left: Main Hand, Head, Body, Hands, Legs, Feet; right: Off Hand, Ears, Neck, Wrists,
    /// Rings). Each cell is an enable/disable checkbox plus — when per-slot thresholds is on (toggled
    /// on the Settings tab) and the slot is enabled — its own loves threshold to its right. Part of
    /// the "Popularity thresholds" subsection below the Eorzea Collection filters.
    /// </summary>
    private void DrawGearSlots()
    {
        ImGui.TextUnformatted("Gear slots");
        Help("Which gear slots to check glamours for. Uncheck a slot to ignore any associated dropped items.");

        if (!ImGui.BeginTable("##gearslots", 2, ImGuiTableFlags.SizingStretchSame))
            return;

        foreach (var row in GlamSlot.Grid)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            this.DrawSlotCell(row.Left);
            ImGui.TableNextColumn();
            if (row.Right is { } right)
                this.DrawSlotCell(right);
        }

        ImGui.EndTable();
    }

    /// <summary>Draws one grid cell: the slot's enable checkbox and, in per-slot mode, its threshold.</summary>
    private void DrawSlotCell(GlamSlot slot)
    {
        var enabled = this.actions.IsSlotEnabled(slot);
        if (ImGui.Checkbox($"{slot.Label}##slot-{slot.Key}", ref enabled))
        {
            this.log.Debug($"filter changed: slot '{slot.Key}' enabled = {enabled}.");
            this.actions.SetSlotEnabled(slot, enabled);
        }

        // The per-slot threshold sits to the right of an enabled slot while advanced mode is on.
        // Matches the global control's +/- step buttons (10 / 100); the width leaves room for them.
        if (this.config.PerSlotThresholds && enabled)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150f);
            var slotThreshold = this.actions.GetSlotThreshold(slot);
            if (ImGui.InputInt($"Loves##threshold-{slot.Key}", ref slotThreshold, 10, 100, default))
            {
                this.actions.SetSlotThreshold(slot, slotThreshold);
                this.log.Debug($"filter changed: slot '{slot.Key}' threshold = {this.actions.GetSlotThreshold(slot)}.");
            }
        }
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
