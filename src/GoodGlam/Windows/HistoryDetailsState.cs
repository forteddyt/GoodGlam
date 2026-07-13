using GoodGlam.History;

namespace GoodGlam.Windows;

internal sealed class HistoryDetailsState
{
    public PopularDropRecord? Selected { get; private set; }

    public void Open(PopularDropRecord record) => this.Selected = record;

    public void Close() => this.Selected = null;
}
