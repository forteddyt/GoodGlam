using System.Numerics;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using GoodGlam.Diagnostics;
using GoodGlam.History;

namespace GoodGlam.Windows;

/// <summary>
/// A small, frameless floating button that shows the GoodGlam logo (the brand mark adapted from
/// Eorzea Collection). It is only drawn once a character is logged in (see
/// <see cref="DrawConditions"/>), so it stays hidden on the title / character-select screen.
/// Clicking it opens the GoodGlam window. The logo is drawn from an embedded high-resolution PNG,
/// scaled by the current UI/DPI factor so it stays crisp and correctly sized on any monitor. The
/// window is draggable; ImGui persists its position by window id. A right-click context menu
/// exposes opening the window and a hide option.
/// </summary>
public sealed class LogoWindow : Window, IDisposable
{
    internal const string LogoResourceName = "GoodGlam.Assets.Logo.png";

    /// <summary>Logical (unscaled) edge length of the button, in pixels. Scaled by GlobalScale.</summary>
    private const float BaseButtonSize = 32f;

    /// <summary>Corner rounding of the hover/active darkening behind the logo, in logical pixels.</summary>
    private const float HoverHighlightRounding = 3f;

    private static readonly Assembly OwnAssembly = typeof(LogoWindow).Assembly;

    private readonly Configuration config;
    private readonly Action openMain;
    private readonly NotificationState notificationState;
    private readonly ITraceLogger<LogoWindow> log = new TraceLogger<LogoWindow>();

    /// <summary>Pure pointer-interaction logic (drag/lock/click decisions); see LogoInteraction.</summary>
    private readonly LogoInteraction interaction = new();

    /// <summary>Pure pulse maths driving the unseen-drop notification glow; see NotificationGlow.</summary>
    private readonly NotificationGlow glow = new();

    /// <summary>
    /// A gold silhouette of the logo, baked at runtime from the logo's own pixels and drawn as the
    /// notification halo. Derived from the art itself, so it matches whatever shape the logo is —
    /// no hardcoded geometry to fall out of sync. Null until the async bake finishes (or if it failed).
    /// </summary>
    private IDalamudTextureWrap? glowTexture;
    private bool glowBakeStarted;
    private bool disposed;

    /// <summary>
    /// Guards the hand-off of <see cref="glowTexture"/> between the background bake task and
    /// <see cref="Dispose"/> (which run on different threads), so the baked texture is either
    /// published to the window or disposed exactly once — never leaked, never double-freed.
    /// </summary>
    private readonly object glowLock = new();

    public LogoWindow(Configuration config, Action openMain, NotificationState notificationState)
        : base("GoodGlam###GoodGlamLogo",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoMove)
    {
        this.config = config;
        this.openMain = openMain;
        this.notificationState = notificationState;

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

    [ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering + thin wiring; the drag/click/tooltip decisions live in the tested LogoInteraction and the menu actions (ToggleLock/Hide) are tested. Needs a live ImGui context that can't run in CI.")]
    public override void Draw()
    {
        // Bake the gold halo sprite from the logo's own pixels on the first frame (the render/GPU
        // path is guaranteed live by now), so it's ready well before any drop fires the glow.
        this.EnsureGlowBaked();

        var wrap = Services.TextureProvider.GetFromManifestResource(OwnAssembly, LogoResourceName).GetWrapOrEmpty();

        var size = new Vector2(BaseButtonSize, BaseButtonSize) * ImGuiHelpers.GlobalScale;
        var transparent = new Vector4(0, 0, 0, 0);

        // Suppress ImGui's built-in button fill for every state and draw the hover/active darkening
        // ourselves (below), on the background draw list. That keeps the notification glow — also on
        // the background list — layered *above* the darkening instead of being painted over by it.
        ImGui.PushStyleColor(ImGuiCol.Button, transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, transparent);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, transparent);
        var clicked = ImGui.ImageButton(wrap.Handle, size, Vector2.Zero, Vector2.One, 0, transparent);
        ImGui.PopStyleColor(3);

        var logoMin = ImGui.GetItemRectMin();
        var logoMax = ImGui.GetItemRectMax();

        // Hover/active dimming behind the logo (the feedback the default button used to give), drawn
        // first so the glow sits on top of it.
        this.DrawHoverHighlight(logoMin, logoMax);

        // An unseen popular drop lights the logo with a pulsing golden glow until history is opened.
        if (this.notificationState.HasUnseen)
            this.DrawNotificationGlow(logoMin, logoMax);

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
            ImGui.SetTooltip("GoodGlam — open window (right-click for more)");

        if (outcome.OpenWindow)
        {
            this.log.Debug("logo clicked; opening the GoodGlam window.");
            this.openMain();
        }

        if (ImGui.BeginPopupContextItem("##GoodGlamLogoContext"))
        {
            if (ImGui.MenuItem("Open GoodGlam"))
            {
                this.log.Debug("logo context menu: 'Open GoodGlam' selected.");
                this.openMain();
            }

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

    /// <summary>
    /// Draws the hover/active darkening behind the logo, replicating the feedback the default button
    /// background used to give. Rendered on the background draw list (beneath the notification glow)
    /// so the glow is never painted over by it; the crisp logo on the window still sits on top.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Pure ImGui rendering; requires a live ImGui context that can't run in CI.")]
    private void DrawHoverHighlight(Vector2 logoMin, Vector2 logoMax)
    {
        uint color;
        if (ImGui.IsItemActive())
            color = ImGui.GetColorU32(ImGuiCol.ButtonActive);
        else if (ImGui.IsItemHovered())
            color = ImGui.GetColorU32(ImGuiCol.ButtonHovered);
        else
            return;

        ImGui.GetBackgroundDrawList()
            .AddRectFilled(logoMin, logoMax, color, HoverHighlightRounding * ImGuiHelpers.GlobalScale);
    }

    /// <summary>
    /// Draws a soft, pulsing golden glow that hugs the logo's actual silhouette to flag an unseen
    /// popular drop. The gold sprite (baked from the logo's own alpha — see <see cref="EnsureGlowBaked"/>)
    /// is stamped at a ring of small radial offsets around the logo so the gold "bleeds" outward
    /// around the real shape, whatever it is. Rendered on the background draw list so it sits behind
    /// the crisp logo (which keeps it sharp) and isn't clipped by the button's tiny auto-resizing
    /// window. <see cref="NotificationGlow"/> breathes the overall brightness each frame.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Submits ImGui draw-list calls; the pulse/stamp layout it iterates lives in the tested NotificationGlow. Needs a live ImGui context that can't run in CI.")]
    private void DrawNotificationGlow(Vector2 logoMin, Vector2 logoMax)
    {
        // Until the async bake finishes (first couple of frames after load), there's simply no glow
        // yet; it's ready long before any real drop can fire.
        if (this.glowTexture is not { } sprite)
            return;

        var drawList = ImGui.GetBackgroundDrawList();

        // The stamp layout/alpha (pure, unit-tested in NotificationGlow) hugs the silhouette; here we
        // just submit each stamp of the gold sprite around the logo rect.
        foreach (var stamp in this.glow.BuildStamps(ImGui.GetTime(), ImGuiHelpers.GlobalScale))
        {
            var tint = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, stamp.Alpha));
            drawList.AddImage(
                sprite.Handle,
                logoMin + stamp.Offset,
                logoMax + stamp.Offset,
                Vector2.Zero,
                Vector2.One,
                tint);
        }
    }

    /// <summary>
    /// Kicks off the one-time, async bake of the gold halo sprite from the logo's own pixels: decode
    /// the embedded PNG, read its alpha back, recolour to gold (<see cref="NotificationGlow.BuildGoldSilhouette"/>),
    /// and upload the result. Deriving the halo from the art means it always matches the current logo
    /// shape — change the logo and the glow follows, with no geometry to update.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Guards/kicks off the GPU texture bake; only reachable from the ImGui Draw path.")]
    private void EnsureGlowBaked()
    {
        if (this.glowBakeStarted)
            return;

        this.glowBakeStarted = true;
        _ = this.BakeGlowTextureAsync();
    }

    [ExcludeFromCodeCoverage(Justification = "Decodes the logo and uploads a GPU texture via ITextureProvider; needs a live render device.")]
    private async Task BakeGlowTextureAsync()
    {
        try
        {
            this.log.Debug("baking the notification glow sprite from the logo art.");
            await using var stream = OwnAssembly.GetManifestResourceStream(LogoResourceName)
                ?? throw new InvalidOperationException($"Embedded logo resource '{LogoResourceName}' is missing.");

            using var source = await Services.TextureProvider
                .CreateFromImageAsync(stream, leaveOpen: true, "GoodGlam.NotificationGlow.src")
                .ConfigureAwait(false);

            var readback = (ITextureReadbackProvider)Services.TextureProvider;
            var (spec, pixels) = await readback
                .GetRawImageAsync(source, leaveWrapOpen: true)
                .ConfigureAwait(false);

            var gold = NotificationGlow.BuildGoldSilhouette(spec.Width, spec.Height, spec.Pitch, pixels);
            var baked = Services.TextureProvider.CreateFromRaw(
                RawImageSpecification.Bgra32(spec.Width, spec.Height), gold, "GoodGlam.NotificationGlow");

            // Publish under the lock so we race with Dispose() exactly once: either we hand the
            // texture to the window (and Dispose frees it later), or disposal already happened and
            // we free the freshly-baked texture here. The lock also barriers the `disposed` read.
            bool published;
            lock (this.glowLock)
            {
                published = !this.disposed;
                if (published)
                    this.glowTexture = baked;
            }

            if (!published)
                baked.Dispose();
            else
                this.log.Verbose($"notification glow sprite baked ({spec.Width}x{spec.Height}).");
        }
        catch (Exception ex)
        {
            this.log.Warning("failed to bake the notification glow sprite; the logo won't glow.", ex);
        }
    }

    /// <summary>Toggles whether the floating button can be dragged, and persists the choice.</summary>
    internal void ToggleLock()
    {
        this.config.LockLogo = !this.config.LockLogo;
        this.log.Debug($"logo position {(this.config.LockLogo ? "locked" : "unlocked")}.");
        this.config.Save();
    }

    /// <summary>Hides the floating button and remembers the choice (re-enable from settings).</summary>
    internal void Hide()
    {
        this.log.Debug("logo hidden via its context menu.");
        this.config.ShowLogo = false;
        this.config.Save();
        this.IsOpen = false;
    }

    /// <summary>Releases the baked glow sprite (an owned GPU texture) when the plugin unloads.</summary>
    public void Dispose()
    {
        // Capture and clear under the lock so a bake task finishing concurrently either sees
        // `disposed` and frees its own texture, or has already published the one we dispose here.
        IDalamudTextureWrap? toDispose;
        lock (this.glowLock)
        {
            this.disposed = true;
            toDispose = this.glowTexture;
            this.glowTexture = null;
        }

        toDispose?.Dispose();
    }
}
