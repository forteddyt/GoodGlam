namespace GoodGlam.History;

/// <summary>
/// A tiny, thread-safe latch for "there is an unseen popular drop". The notifier (which runs off
/// the framework thread) calls <see cref="Raise"/>; the floating logo reads <see cref="HasUnseen"/>
/// on the UI thread to decide whether to draw its notification glow; opening the history window
/// calls <see cref="Clear"/>. A single <see langword="volatile"/> field is all the synchronisation
/// a one-way boolean signal needs.
/// </summary>
public sealed class NotificationState
{
    private volatile bool hasUnseen;

    /// <summary>Whether an unseen popular drop is waiting to be acknowledged.</summary>
    public bool HasUnseen => this.hasUnseen;

    /// <summary>Marks that a popular drop has arrived (lights up the logo glow).</summary>
    public void Raise() => this.hasUnseen = true;

    /// <summary>Clears the signal (called when the history window is opened).</summary>
    public void Clear() => this.hasUnseen = false;
}
