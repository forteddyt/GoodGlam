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

    public MainWindow(Configuration config, EcFilterCatalog filterCatalog, NotificationHistoryStore store, Action<bool> setLogoVisible)
        : base("GoodGlam###GoodGlamMain")
    {
        this.historyTab = new HistoryTab(store);
        this.settingsTab = new SettingsTab(config, filterCatalog, setLogoVisible);
        this.Size = new Vector2(560, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##GoodGlamTabs"))
            return;

        // History is listed first so it is the default landing tab whenever the window opens.
        if (ImGui.BeginTabItem("History"))
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
