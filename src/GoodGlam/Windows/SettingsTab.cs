using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using System.Diagnostics.CodeAnalysis;
using GoodGlam.Diagnostics;
using GoodGlam.Localization;

namespace GoodGlam.Windows;

/// <summary>
/// The Settings tab of the unified <see cref="MainWindow"/>: plugin configuration only — the
/// floating-logo toggle, the drop-notification master switch, the <b>Per-slot loves thresholds</b>
/// advanced toggle, the cache lifetime, and <b>Reset Settings</b> (which reverts just these
/// Settings-tab controls). The Eorzea Collection filter set and the per-slot values themselves live
/// in the <see cref="FiltersTab"/>, and the version/logo/links/feedback in the
/// <see cref="AboutTab"/>, so this tab is single-purpose plugin config.
/// </summary>
/// <remarks>
/// Rendering only. Every control's effect (config mutation, clamping, restore) lives in the pure,
/// unit-tested <see cref="SettingsActions"/>; each widget here is thin wiring that reads an ImGui
/// control and calls one action. That's why this class is excluded from coverage while the behavior
/// behind it is fully tested.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering + thin wiring; the control effects are extracted into the tested SettingsActions, and a live ImGui context can't run in CI.")]
internal sealed class SettingsTab
{
    private readonly Configuration config;
    private readonly SettingsActions actions;
    private readonly ITraceLogger<SettingsTab> log = new TraceLogger<SettingsTab>();

    internal SettingsTab(Configuration config, SettingsActions actions)
    {
        this.config = config;
        this.actions = actions;
    }

    internal void Draw()
    {
        var settings = Loc.Strings.Settings;

        var showLogo = this.config.ShowLogo;
        if (ImGui.Checkbox(settings.ShowLogo, ref showLogo))
            this.actions.SetShowLogo(showLogo); // logged in Plugin.SetLogoVisible
        Help(settings.ShowLogoHelp);

        ImGui.Separator();

        var enabled = this.config.Enabled;
        if (ImGui.Checkbox(settings.EnableNotifications, ref enabled))
        {
            this.log.Debug($"setting changed: Enabled = {enabled}.");
            this.actions.SetEnabled(enabled);
        }

        Help(settings.EnableNotificationsHelp);

        ImGui.Separator();

        var perSlot = this.config.PerSlotThresholds;
        if (ImGui.Checkbox(settings.PerSlotThresholds, ref perSlot))
        {
            this.log.Debug($"setting changed: PerSlotThresholds = {perSlot}.");
            this.actions.SetPerSlotThresholds(perSlot);
        }

        Help(settings.PerSlotThresholdsHelp);

        ImGui.Separator();

        var ttl = this.config.CacheTtlHours;
        if (ImGui.InputInt(settings.CacheLifetime, ref ttl, 1, 6, default))
        {
            this.actions.SetCacheTtlHours(ttl);
            this.log.Debug($"setting changed: CacheTtlHours = {this.config.CacheTtlHours}.");
        }

        Help(settings.CacheLifetimeHelp);

        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button(settings.ResetButton))
        {
            this.log.Debug("Reset Settings clicked; resetting the Settings-tab controls.");
            this.actions.ResetSettings();
        }

        Help(settings.ResetHelp);
    }

    /// <summary>Draws Dalamud's standard info "(?)" icon on the same line, with a hover tooltip.</summary>
    private static void Help(string text)
    {
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(text);
    }
}
