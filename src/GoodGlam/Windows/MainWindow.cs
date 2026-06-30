using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GoodGlam.Glam;
using GoodGlam.History;

namespace GoodGlam.Windows;

/// <summary>
/// The single GoodGlam window. It hosts the History and Settings views as tabs so the plugin
/// presents one consolidated panel instead of two independently-positioned windows. Every entry
/// point (the floating logo, the Dalamud config gear, and the slash command) opens this window on
/// the History tab; Settings is reached via the tab bar.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly HistoryTab historyTab;
    private readonly SettingsTab settingsTab;

    /// <summary>
    /// Drives force-selecting the History tab on each open (see <see cref="HistoryTabFocus"/>).
    /// ImGui persists a tab bar's active tab in its own state, which Dalamud does not reset when the
    /// window is hidden — so without this, reopening after a visit to Settings would land back on
    /// Settings, breaking the "always open on History" contract (and letting the glow be
    /// acknowledged without the new drop ever being seen).
    /// </summary>
    private readonly HistoryTabFocus historyFocus = new();

    public MainWindow(Configuration config, EcFilterCatalog filterCatalog, NotificationHistoryStore store, Action<bool> setLogoVisible)
        : base("GoodGlam###GoodGlamMain")
    {
        this.historyTab = new HistoryTab(store);
        this.settingsTab = new SettingsTab(config, filterCatalog, setLogoVisible);
        this.Size = new Vector2(560, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Land on the History tab every time the window opens (see <see cref="HistoryTabFocus"/>).</summary>
    public override void OnOpen() => this.historyFocus.OnOpen();

    public override void Draw()
    {
        // All GoodGlam metadata is per-character, so there's nothing to show or edit until a
        // character is logged in. Present a read-only note instead of the tabs on the title /
        // character-select screen.
        if (!Services.ClientState.IsLoggedIn)
        {
            ImGui.TextWrapped("Log in to a character to view or change GoodGlam. " +
                "Each character keeps its own history, filters, and settings.");
            return;
        }

        if (!ImGui.BeginTabBar("##GoodGlamTabs"))
            return;

        // History is listed first and force-selected on each open so it is the default landing tab.
        var historyFlags = this.historyFocus.ConsumeForceSelect()
            ? ImGuiTabItemFlags.SetSelected
            : ImGuiTabItemFlags.None;

        if (ImGui.BeginTabItem("History", historyFlags))
        {
            this.historyTab.Draw();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Settings"))
        {
            this.settingsTab.Draw();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }
}
