using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;
using GoodGlam.History;

namespace GoodGlam.Windows;

/// <summary>
/// The single GoodGlam window. It hosts four tabs — History, Filters, Settings, and About — so the
/// plugin presents one consolidated panel instead of several independently-positioned windows. Every
/// entry point (the floating logo, the Dalamud config gear, and the slash command) opens this window
/// on the History tab; the other tabs are reached via the tab bar.
/// </summary>
public sealed class MainWindow : Window
{
    /// <summary>
    /// The tab labels in display order. History is first (and force-selected on open); the rest —
    /// Filters, Settings, About — follow. Exposed so the ordering contract is unit-testable without a
    /// live ImGui context, and used to label the tab items in <see cref="Draw"/>.
    /// </summary>
    internal static readonly string[] TabOrder = ["History", "Filters", "Settings", "About"];

    private readonly HistoryTab historyTab;
    private readonly FiltersTab filtersTab;
    private readonly SettingsTab settingsTab;
    private readonly AboutTab aboutTab;
    private readonly ITraceLogger<MainWindow> log = new TraceLogger<MainWindow>();

    /// <summary>
    /// Drives force-selecting the History tab on each open (see <see cref="HistoryTabFocus"/>).
    /// ImGui persists a tab bar's active tab in its own state, which Dalamud does not reset when the
    /// window is hidden — so without this, reopening after a visit to another tab would land back
    /// there, breaking the "always open on History" contract (and letting the glow be acknowledged
    /// without the new drop ever being seen).
    /// </summary>
    private readonly HistoryTabFocus historyFocus = new();

    public MainWindow(Configuration config, EcFilterCatalog filterCatalog, NotificationHistoryStore store, Action<bool> setLogoVisible)
        : base("GoodGlam###GoodGlamMain")
    {
        // One shared actions instance backs both the Settings and Filters tabs (config mutation,
        // clamping, restore/reset all live there); the logo callback is only relevant to Settings.
        var actions = new SettingsActions(config, setLogoVisible);
        this.historyTab = new HistoryTab(store);
        this.filtersTab = new FiltersTab(config, filterCatalog, actions);
        this.settingsTab = new SettingsTab(config, actions);
        this.aboutTab = new AboutTab();
        this.Size = new Vector2(560, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Land on the History tab every time the window opens (see <see cref="HistoryTabFocus"/>).</summary>
    public override void OnOpen()
    {
        this.log.Debug("window opened; forcing the History tab.");
        this.historyFocus.OnOpen();
    }

    [ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; requires a live ImGui context that can't run in CI.")]
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

        if (ImGui.BeginTabItem(TabOrder[0], historyFlags))
        {
            this.historyTab.Draw();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(TabOrder[1]))
        {
            this.filtersTab.Draw();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(TabOrder[2]))
        {
            this.settingsTab.Draw();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(TabOrder[3]))
        {
            this.aboutTab.Draw();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }
}
