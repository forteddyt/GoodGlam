namespace GoodGlam.Windows;

/// <summary>
/// The destination tabs supported by direct window entry points.
/// </summary>
internal enum MainTab
{
    History,
    Filters,
    Settings,
    About,
}

/// <summary>
/// Pure, ImGui-free one-shot tab selection. ImGui retains a tab bar's active tab while the window is
/// hidden, so direct History and Settings entry points request their destination for the next frame.
/// Once that tab consumes the request, manual tab selection is left alone.
/// </summary>
internal sealed class MainTabFocus
{
    private MainTab? pending = MainTab.History;

    internal MainTab? Pending => this.pending;

    internal void Request(MainTab tab) => this.pending = tab;

    internal bool Consume(MainTab tab)
    {
        if (this.pending != tab)
            return false;

        this.pending = null;
        return true;
    }
}
