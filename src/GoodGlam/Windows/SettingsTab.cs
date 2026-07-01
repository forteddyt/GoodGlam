using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using System.Diagnostics.CodeAnalysis;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;

namespace GoodGlam.Windows;

/// <summary>
/// The Settings tab of the unified <see cref="MainWindow"/>: notification toggle, loves threshold,
/// cache lifetime, the EC filter controls, and the floating-logo toggle. (Formerly the standalone
/// ConfigWindow; the old "Open history" button is gone now that History is a sibling tab.)
/// </summary>
/// <remarks>
/// Rendering only. Every control's effect (config mutation, clamping, restore/reset) lives in the
/// pure, unit-tested <see cref="SettingsActions"/>; each widget here is thin wiring that reads an
/// ImGui control and calls one action. That's why this class is excluded from coverage while the
/// behavior behind it is fully tested.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering + thin wiring; the control effects are extracted into the tested SettingsActions, and a live ImGui context can't run in CI.")]
internal sealed class SettingsTab
{
    private readonly Configuration config;
    private readonly EcFilterCatalog filterCatalog;
    private readonly SettingsActions actions;
    private readonly ILinkOpener linkOpener;
    private readonly ITraceLogger<SettingsTab> log = new TraceLogger<SettingsTab>();

    internal SettingsTab(Configuration config, EcFilterCatalog filterCatalog, Action<bool> setLogoVisible)
        : this(config, filterCatalog, setLogoVisible, new DalamudLinkOpener())
    {
    }

    internal SettingsTab(Configuration config, EcFilterCatalog filterCatalog, Action<bool> setLogoVisible, ILinkOpener linkOpener)
    {
        this.config = config;
        this.filterCatalog = filterCatalog;
        this.actions = new SettingsActions(config, setLogoVisible);
        this.linkOpener = linkOpener;
    }

    internal void Draw()
    {
        var showLogo = this.config.ShowLogo;
        if (ImGui.Checkbox("Show floating logo button", ref showLogo))
            this.actions.SetShowLogo(showLogo); // logged in Plugin.SetLogoVisible
        Help("Shows a small draggable GoodGlam logo in-game; click it to open the GoodGlam window.");

        ImGui.Separator();

        var enabled = this.config.Enabled;
        if (ImGui.Checkbox("Enable drop notifications", ref enabled))
        {
            this.log.Debug($"setting changed: Enabled = {enabled}.");
            this.actions.SetEnabled(enabled);
        }

        Help("Master switch. When off, GoodGlam never checks dropped items or logs popular drops.");

        ImGui.Separator();
        ImGui.TextWrapped(
            "Notify when a dropped item is used in a glamour with at least this many loves on Eorzea Collection:");

        var threshold = this.config.LovesThreshold;
        if (ImGui.InputInt("Loves threshold", ref threshold, 10, 100, default))
        {
            this.actions.SetLovesThreshold(threshold);
            this.log.Debug($"setting changed: LovesThreshold = {this.config.LovesThreshold}.");
        }

        Help("Minimum 'loves' a glamour must have for a drop to count as popular. Higher = pickier.");

        var ttl = this.config.CacheTtlHours;
        if (ImGui.InputInt("Cache lifetime (hours)", ref ttl, 1, 6, default))
        {
            this.actions.SetCacheTtlHours(ttl);
            this.log.Debug($"setting changed: CacheTtlHours = {this.config.CacheTtlHours}.");
        }

        Help("How long a popularity result is reused before re-checking Eorzea Collection. " +
            "Longer = fewer requests, but slower to reflect new glamours. Clamped to 1-72 hours.");

        ImGui.Separator();
        this.DrawFilters();

        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button("Restore Defaults"))
        {
            this.log.Debug("Restore Defaults clicked; resetting all settings and filters.");
            this.actions.RestoreDefaults();
        }

        Help("Reverts every GoodGlam setting (notifications, threshold, cache, and all filters) to defaults.");

        // A small "Feedback" section at the bottom of Settings, modeled on the Restore Defaults row.
        ImGui.Separator();
        ImGui.TextDisabled("Feedback");
        Feedback.DrawReportBugButton(this.linkOpener);
        Help("Found a problem? Report it!");
    }

    /// <summary>Draws Dalamud's standard info "(?)" icon on the same line, with a hover tooltip.</summary>
    private static void Help(string text)
    {
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(text);
    }

    private void DrawFilters()
    {
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

        Help("Clears only the filters above; keeps notifications, threshold, and cache settings.");
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
