using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using System.Diagnostics.CodeAnalysis;
using GoodGlam.Diagnostics;

namespace GoodGlam.Windows;

/// <summary>
/// The Settings tab of the unified <see cref="MainWindow"/>: plugin configuration only — the
/// floating-logo toggle, the drop-notification master switch, the loves threshold, the cache
/// lifetime, and <b>Restore Defaults</b>. The Eorzea Collection filter set now lives in its own
/// <see cref="FiltersTab"/>, and the version/logo/links/feedback in the <see cref="AboutTab"/>, so
/// this tab is single-purpose plugin config.
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

        var ttl = this.config.CacheTtlHours;
        if (ImGui.InputInt("Cache lifetime (hours)", ref ttl, 1, 6, default))
        {
            this.actions.SetCacheTtlHours(ttl);
            this.log.Debug($"setting changed: CacheTtlHours = {this.config.CacheTtlHours}.");
        }

        Help("How long a popularity result is reused before re-checking Eorzea Collection. " +
            "Longer = fewer requests, but slower to reflect new glamours. Clamped to 1-72 hours.");

        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button("Restore Defaults"))
        {
            this.log.Debug("Restore Defaults clicked; resetting all settings and filters.");
            this.actions.RestoreDefaults();
        }

        Help("Reverts every GoodGlam setting (notifications, threshold, cache, and all filters) to defaults.");
    }

    /// <summary>Draws Dalamud's standard info "(?)" icon on the same line, with a hover tooltip.</summary>
    private static void Help(string text)
    {
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(text);
    }
}
