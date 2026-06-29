namespace GoodGlam.Windows;

/// <summary>
/// Pure, ImGui-free decision logic for the floating logo button's pointer interaction. Kept
/// separate from <see cref="LogoWindow"/> so the lock/drag/click behaviour can be unit tested
/// without a running ImGui context. <see cref="LogoWindow.Draw"/> feeds it the current frame's
/// ImGui state and acts on the returned decisions.
/// </summary>
internal sealed class LogoInteraction
{
    /// <summary>True once the current press has moved far enough to count as a drag.</summary>
    private bool dragging;

    /// <summary>Exposed for assertions/diagnostics: whether a drag is currently latched.</summary>
    internal bool Dragging => this.dragging;

    /// <summary>One frame of ImGui pointer state relevant to the logo button.</summary>
    /// <param name="Held">The logo button is the active (pressed) item this frame.</param>
    /// <param name="MouseDragging">The left mouse button is being dragged this frame.</param>
    /// <param name="Clicked">The logo button reported a click this frame.</param>
    /// <param name="MouseReleased">The left mouse button was released this frame.</param>
    /// <param name="Locked">The logo position is locked (dragging disabled).</param>
    internal readonly record struct Input(
        bool Held, bool MouseDragging, bool Clicked, bool MouseReleased, bool Locked);

    /// <summary>The actions the window should take for a frame.</summary>
    /// <param name="MoveWindow">Move the window by the current mouse delta.</param>
    /// <param name="OpenHistory">Treat this frame as a click that opens the history window.</param>
    /// <param name="AllowTooltip">Whether the hover tooltip may be shown (suppressed mid-drag).</param>
    internal readonly record struct Output(bool MoveWindow, bool OpenHistory, bool AllowTooltip);

    /// <summary>
    /// Advances the interaction by one frame. When unlocked and the held button is being dragged,
    /// the window should move and the drag latch is set so the trailing click is suppressed. A
    /// locked logo never drags, so a held press stays a plain click. The latch clears on release.
    /// </summary>
    internal Output Process(in Input input)
    {
        var moveWindow = false;
        if (!input.Locked && input.Held && input.MouseDragging)
        {
            this.dragging = true;
            moveWindow = true;
        }

        var allowTooltip = !this.dragging;
        var openHistory = input.Clicked && !this.dragging;

        if (input.MouseReleased)
            this.dragging = false;

        return new Output(moveWindow, openHistory, allowTooltip);
    }
}
