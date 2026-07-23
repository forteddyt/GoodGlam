using System.Numerics;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;
using GoodGlam.History;
using GoodGlam.Localization;

namespace GoodGlam.Windows;

/// <summary>
/// The single GoodGlam window. It hosts four tabs — History, Filters, Settings, and About — so the
/// plugin presents one consolidated panel instead of several independently-positioned windows. Every
/// entry point chooses an explicit destination: ordinary Open/history actions land on History, while
/// Dalamud Settings and settings/config slash commands land on Settings.
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
    /// The tab labels in display order, sourced from the string catalog. History is first; direct
    /// entry points can request History or Settings for the next frame. Exposed so the ordering
    /// contract is unit-testable without a live ImGui context, and used to label the tab items in
    /// <see cref="Draw"/>.
    /// </summary>
    internal static string[] TabOrder =>
        [Loc.Strings.Tabs.History, Loc.Strings.Tabs.Filters, Loc.Strings.Tabs.Settings, Loc.Strings.Tabs.About];

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
    /// Drives a one-shot History or Settings destination request for direct entry points.
    /// </summary>
    private readonly MainTabFocus tabFocus = new();

    internal MainWindow(
        Configuration config,
        EcFilterCatalog filterCatalog,
        NotificationHistoryStore store,
        Action<bool> setLogoVisible,
        DropDetailsWindow detailsWindow)
        : base($"{Loc.Strings.Common.AppName}###GoodGlamMain")
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

    internal MainTab? PendingTab => this.tabFocus.Pending;

    internal void OpenTab(MainTab tab)
    {
        this.log.Debug($"opening the window on the {tab} tab.");
        this.detailsWindow.Close();
        this.tabFocus.Request(tab);
        this.IsOpen = true;
    }

    public override void OnOpen()
    {
        this.log.Debug("window opened.");
        this.historyTab.CloseDetails();
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
            ImGui.TextWrapped(Loc.Strings.MainWindow.LoginPrompt);
            return;
        }

        if (!ImGui.BeginTabBar("##GoodGlamTabs"))
            return;

        var tabs = TabOrder;
        if (ImGui.BeginTabItem(tabs[0], this.TabFlags(MainTab.History)))
        {
            DrawTabBody(0, this.historyTab.Draw);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(tabs[1], this.TabFlags(MainTab.Filters)))
        {
            this.historyTab.CloseDetails();
            DrawTabBody(1, this.filtersTab.Draw);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(tabs[2], this.TabFlags(MainTab.Settings)))
        {
            this.historyTab.CloseDetails();
            DrawTabBody(2, this.settingsTab.Draw);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(tabs[3], this.TabFlags(MainTab.About)))
        {
            this.historyTab.CloseDetails();
            DrawTabBody(3, this.aboutTab.Draw);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private ImGuiTabItemFlags TabFlags(MainTab tab)
        => this.tabFocus.Consume(tab) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

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
