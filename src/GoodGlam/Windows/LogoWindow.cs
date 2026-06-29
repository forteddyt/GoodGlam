using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace GoodGlam.Windows;

/// <summary>
/// A small, frameless floating button that shows the GoodGlam logo (the brand mark adapted from
/// Eorzea Collection). It is only drawn once a character is logged in (see
/// <see cref="DrawConditions"/>), so it stays hidden on the title / character-select screen.
/// Clicking it opens the history window. The logo is drawn from an embedded high-resolution PNG,
/// scaled by the current UI/DPI factor so it stays crisp and correctly sized on any monitor. The
/// window is draggable; ImGui persists its position by window id. A right-click context menu
/// exposes settings and a hide option.
/// </summary>
public sealed class LogoWindow : Window
{
    private const string LogoResourceName = "GoodGlam.Assets.Logo.png";

    /// <summary>Logical (unscaled) edge length of the button, in pixels. Scaled by GlobalScale.</summary>
    private const float BaseButtonSize = 32f;

    private static readonly Assembly OwnAssembly = typeof(LogoWindow).Assembly;

    private readonly Configuration config;
    private readonly Action openHistory;
    private readonly Action openConfig;

    /// <summary>Pure pointer-interaction logic (drag/lock/click decisions); see LogoInteraction.</summary>
    private readonly LogoInteraction interaction = new();

    public LogoWindow(Configuration config, Action openHistory, Action openConfig)
        : base("GoodGlam###GoodGlamLogo",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoMove)
    {
        this.config = config;
        this.openHistory = openHistory;
        this.openConfig = openConfig;

        // It's a toolbar widget, not a dialog: Escape shouldn't close it and it shouldn't click-clack.
        this.RespectCloseHotkey = false;
        this.DisableWindowSounds = true;
    }

    /// <summary>
    /// Only draw the logo while a character is logged in, so it stays hidden on the title /
    /// character-select screen. <see cref="Window.IsOpen"/> still reflects the user's show/hide
    /// preference; this gates the in-world visibility on top of it.
    /// </summary>
    public override bool DrawConditions() => Services.ClientState.IsLoggedIn;

    public override void Draw()
    {
        var wrap = Services.TextureProvider.GetFromManifestResource(OwnAssembly, LogoResourceName).GetWrapOrEmpty();

        var size = new Vector2(BaseButtonSize, BaseButtonSize) * ImGuiHelpers.GlobalScale;
        var transparent = new Vector4(0, 0, 0, 0);

        // Transparent resting background so only the mark shows; hover/active still give feedback.
        ImGui.PushStyleColor(ImGuiCol.Button, transparent);
        var clicked = ImGui.ImageButton(wrap.Handle, size, Vector2.Zero, Vector2.One, 0, transparent);
        ImGui.PopStyleColor();

        // All movement is handled manually below via SetWindowPos. ImGui's built-in window move is
        // disabled (NoMove) so it can't grab the window's padding border and bypass the lock — that
        // border exists because AlwaysAutoResize keeps the default WindowPadding around the button.
        // Drive the move/click/tooltip decisions through LogoInteraction (pure, testable) and act on
        // its result here against live ImGui state.
        var outcome = this.interaction.Process(new LogoInteraction.Input(
            Held: ImGui.IsItemActive(),
            MouseDragging: ImGui.IsMouseDragging(ImGuiMouseButton.Left),
            Clicked: clicked,
            MouseReleased: ImGui.IsMouseReleased(ImGuiMouseButton.Left),
            Locked: this.config.LockLogo));

        if (outcome.MoveWindow)
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (delta != Vector2.Zero)
                ImGui.SetWindowPos(ImGui.GetWindowPos() + delta, ImGuiCond.Always);
        }

        if (outcome.AllowTooltip && ImGui.IsItemHovered())
            ImGui.SetTooltip("GoodGlam — open history (right-click for more)");

        if (outcome.OpenHistory)
            this.openHistory();

        if (ImGui.BeginPopupContextItem("##GoodGlamLogoContext"))
        {
            if (ImGui.MenuItem("Open history"))
                this.openHistory();

            if (ImGui.MenuItem("Open settings"))
                this.openConfig();

            ImGui.Separator();

            // Checkable: fixed label with a check shown when the position is locked.
            if (ImGui.MenuItem("Lock position", string.Empty, this.config.LockLogo))
                this.ToggleLock();

            ImGui.Separator();

            if (ImGui.MenuItem("Hide this button"))
                this.Hide();

            ImGui.EndPopup();
        }
    }

    /// <summary>Toggles whether the floating button can be dragged, and persists the choice.</summary>
    internal void ToggleLock()
    {
        this.config.LockLogo = !this.config.LockLogo;
        this.config.Save();
    }

    /// <summary>Hides the floating button and remembers the choice (re-enable from settings).</summary>
    internal void Hide()
    {
        this.config.ShowLogo = false;
        this.config.Save();
        this.IsOpen = false;
    }
}
