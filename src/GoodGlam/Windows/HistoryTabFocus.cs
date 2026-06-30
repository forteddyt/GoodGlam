namespace GoodGlam.Windows;

/// <summary>
/// Pure, ImGui-free decision logic for keeping the <see cref="MainWindow"/> landing on its History
/// tab. ImGui persists a tab bar's active tab in its own state, which Dalamud does not reset when
/// the window is hidden — so without an explicit nudge, reopening after a visit to Settings would
/// reopen on Settings. This tracks the "force History on the next frame" intent so the window can
/// pass <c>ImGuiTabItemFlags.SetSelected</c> to the History tab for exactly one frame per open.
/// Kept separate (like <see cref="LogoInteraction"/>) so the contract is unit-testable without a
/// running ImGui context.
/// </summary>
internal sealed class HistoryTabFocus
{
    /// <summary>True while a force-to-History is pending for the next drawn frame.</summary>
    private bool pending = true;

    /// <summary>Exposed for assertions/diagnostics: whether a force-select is still pending.</summary>
    internal bool Pending => this.pending;

    /// <summary>Records that the window has (re)opened, so the next frame should select History.</summary>
    internal void OnOpen() => this.pending = true;

    /// <summary>
    /// Returns whether the History tab should be force-selected this frame, consuming the intent so
    /// later frames don't keep overriding the user's tab choice. True exactly once per open.
    /// </summary>
    internal bool ConsumeForceSelect()
    {
        var force = this.pending;
        this.pending = false;
        return force;
    }
}
