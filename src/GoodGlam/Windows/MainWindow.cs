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
    /// Set on each open transition so the next <see cref="Draw"/> frame force-selects the History
    /// tab. ImGui persists a tab bar's active tab in its own state (keyed by the tab-bar id), which
    /// Dalamud does not reset when the window is hidden — so without this, reopening after a visit
    /// to Settings would land back on Settings, breaking the "always open on History" contract (and
    /// letting the glow be acknowledged without the new drop ever being seen).
    /// </summary>
    private bool forceHistoryTab = true;

    public MainWindow(Configuration config, EcFilterCatalog filterCatalog, NotificationHistoryStore store, Action<bool> setLogoVisible)
        : base("GoodGlam###GoodGlamMain")
    {
        this.historyTab = new HistoryTab(store);
        this.settingsTab = new SettingsTab(config, filterCatalog, setLogoVisible);
        this.Size = new Vector2(560, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Land on the History tab every time the window opens (see <see cref="forceHistoryTab"/>).</summary>
    public override void OnOpen() => this.forceHistoryTab = true;

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##GoodGlamTabs"))
            return;

        // History is listed first and force-selected on each open so it is the default landing tab.
        var historyFlags = this.forceHistoryTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        this.forceHistoryTab = false;

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
