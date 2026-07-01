namespace GoodGlam.Windows;

/// <summary>How a history table link cell should render, decided without any ImGui dependency.</summary>
internal enum HistoryLinkKind
{
    /// <summary>No URL and no label: show the disabled fallback text.</summary>
    Disabled,

    /// <summary>No URL but a label exists: show it as plain (non-clickable) text.</summary>
    PlainText,

    /// <summary>A URL exists: show a clickable link.</summary>
    Link,
}

/// <summary>
/// Pure rendering decision for a History-tab link cell (glamour name / "Browse" listing): given a
/// label, an optional URL, and a fallback, decide whether it's a clickable link, plain text, or the
/// disabled fallback, and what text to show. Split out of <see cref="HistoryTab"/> so the
/// link-vs-text logic (which must handle older records saved before URLs were captured) is unit-tested
/// without a live ImGui context.
/// </summary>
internal readonly record struct HistoryLinkCell(HistoryLinkKind Kind, string Text, string? Url)
{
    internal static HistoryLinkCell Resolve(string? label, string? url, string fallback)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.IsNullOrEmpty(label)
                ? new HistoryLinkCell(HistoryLinkKind.Disabled, fallback, null)
                : new HistoryLinkCell(HistoryLinkKind.PlainText, label, null);
        }

        return new HistoryLinkCell(HistoryLinkKind.Link, string.IsNullOrEmpty(label) ? url : label, url);
    }
}

/// <summary>
/// The pure effects behind History-tab interactions. Today that's opening an Eorzea Collection link;
/// extracted from <see cref="HistoryTab"/> so "clicking a populated link opens exactly that URL" is
/// unit-testable against a fake <see cref="ILinkOpener"/>.
/// </summary>
internal sealed class HistoryActions
{
    private readonly ILinkOpener linkOpener;

    internal HistoryActions(ILinkOpener linkOpener) => this.linkOpener = linkOpener;

    /// <summary>Opens the given EC link, ignoring a null/empty URL (older records without one).</summary>
    internal void OpenLink(string? url)
    {
        if (!string.IsNullOrEmpty(url))
            this.linkOpener.Open(url);
    }
}
