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
/// <remarks>
/// The tab bar is pinned as a fixed header: each tab body (except History) draws into its own
/// border-less scroll child, so long content scrolls <em>inside</em> the tab while the tab bar stays
/// put above it. History is the exception — its table already owns a <c>ScrollY</c> region with a
/// frozen header, so it needs no extra child (see <see cref="TabScrollRegions"/>).
/// </remarks>
public sealed class MainWindow : Window, IDisposable
{
    /// <summary>
    /// The tab labels in display order. History is first (and force-selected on open); the rest —
    /// Filters, Settings, About — follow. Exposed so the ordering contract is unit-testable without a
    /// live ImGui context, and used to label the tab items in <see cref="Draw"/>.
    /// </summary>
    internal static readonly string[] TabOrder = ["History", "Filters", "Settings", "About"];

    /// <summary>
    /// The ImGui child-region IDs that give each tab its own scroll region beneath the fixed tab bar,
    /// index-aligned to <see cref="TabOrder"/>. History (index 0) is <see langword="null"/>: its table
    /// already draws with <see cref="ImGuiTableFlags.ScrollY"/> and a frozen header, so it owns a
    /// scroll region already — wrapping it in another child would add a redundant double scrollbar.
    /// The other three render plain control stacks, so each scrolls as a whole inside its own child.
    /// Exposed so the scroll-region contract is unit-testable without a live ImGui context.
    /// </summary>
    internal static readonly string?[] TabScrollRegions =
        [null, "##filtersBody", "##settingsBody", "##aboutBody"];

    private readonly HistoryTab historyTab;
    private readonly FiltersTab filtersTab;
    private readonly SettingsTab settingsTab;
    private readonly AboutTab aboutTab;
    private readonly DropDetailsWindow detailsWindow;
    private readonly ITraceLogger<MainWindow> log = new TraceLogger<MainWindow>();

    /// <summary>
    /// Drives force-selecting the History tab on each open (see <see cref="HistoryTabFocus"/>).
    /// ImGui persists a tab bar's active tab in its own state, which Dalamud does not reset when the
    /// window is hidden — so without this, reopening after a visit to another tab would land back
    /// there, breaking the "always open on History" contract (and letting the glow be acknowledged
    /// without the new drop ever being seen).
    /// </summary>
    private readonly HistoryTabFocus historyFocus = new();

    internal MainWindow(
        Configuration config,
        EcFilterCatalog filterCatalog,
        NotificationHistoryStore store,
        Action<bool> setLogoVisible,
        DropDetailsWindow detailsWindow)
        : base("GoodGlam###GoodGlamMain")
    {
        // One shared actions instance backs both the Settings and Filters tabs (config mutation,
        // clamping, restore/reset all live there); the logo callback is only relevant to Settings.
        var actions = new SettingsActions(config, setLogoVisible);
        this.detailsWindow = detailsWindow;
        this.historyTab = new HistoryTab(store, detailsWindow);
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
        this.historyTab.CloseDetails();
        this.historyFocus.OnOpen();
    }

    public override void OnClose() => this.historyTab.CloseDetails();

    public override void Update() => this.detailsWindow.BeginHostFrame();

    /// <summary>Releases the History tab's owned image textures when the plugin unloads.</summary>
    public void Dispose() => this.historyTab.Dispose();

    [ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; requires a live ImGui context that can't run in CI.")]
    public override void Draw()
    {
        // All GoodGlam metadata is per-character, so there's nothing to show or edit until a
        // character is logged in. Present a read-only note instead of the tabs on the title /
        // character-select screen.
        if (!Services.ClientState.IsLoggedIn)
        {
            this.historyTab.CloseDetails();
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
            DrawTabBody(0, this.historyTab.Draw);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(TabOrder[1]))
        {
            this.historyTab.CloseDetails();
            DrawTabBody(1, this.filtersTab.Draw);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(TabOrder[2]))
        {
            this.historyTab.CloseDetails();
            DrawTabBody(2, this.settingsTab.Draw);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(TabOrder[3]))
        {
            this.historyTab.CloseDetails();
            DrawTabBody(3, this.aboutTab.Draw);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    /// <summary>
    /// Renders one tab's body beneath the fixed tab bar. Tabs with a <see cref="TabScrollRegions"/>
    /// entry draw into a border-less child sized to the leftover content region, so their content
    /// scrolls inside the tab body instead of scrolling the whole window (which would drag the tab bar
    /// out of view). A <see langword="null"/> entry (History) draws straight into the window body,
    /// since its table already owns a scroll region — see <see cref="TabScrollRegions"/>. The child is
    /// border-less, so ImGui zeroes its window padding and the layout matches the un-wrapped body.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Pure ImGui layout; the scroll-region map (TabScrollRegions) is tested and a live ImGui context can't run in CI.")]
    private static void DrawTabBody(int tabIndex, Action drawBody)
    {
        var scrollRegionId = TabScrollRegions[tabIndex];
        if (scrollRegionId is null)
        {
            drawBody();
            return;
        }

        if (ImGui.BeginChild(scrollRegionId))
            drawBody();
        ImGui.EndChild();
    }
}
