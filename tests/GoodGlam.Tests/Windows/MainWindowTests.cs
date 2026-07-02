using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.History;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// The unified <see cref="MainWindow"/> is almost entirely ImGui (excluded from coverage), but its
/// constructor, <see cref="MainWindow.OnOpen"/>, and the declared <see cref="MainWindow.TabOrder"/>
/// are framework-free: OnOpen arms the History-tab force-select via the pure
/// <see cref="HistoryTabFocus"/>, and TabOrder pins the four tabs (History first). These cover that
/// non-UI surface.
/// </summary>
public class MainWindowTests
{
    private static MainWindow NewWindow()
        => new(
            new Configuration { Filters = new() },
            EcFilterCatalog.LoadEmbedded(),
            new NotificationHistoryStore(string.Empty),
            _ => { });

    [Fact]
    public void Constructs_without_a_framework()
        => NewWindow().Should().NotBeNull();

    [Fact]
    public void Tab_order_is_history_filters_settings_about()
    {
        MainWindow.TabOrder.Should().Equal("History", "Filters", "Settings", "About");
    }

    [Fact]
    public void History_is_the_first_tab()
    {
        MainWindow.TabOrder.Should().NotBeEmpty();
        MainWindow.TabOrder[0].Should().Be("History");
    }

    [Fact]
    public void OnOpen_arms_history_focus_each_time_without_throwing()
    {
        var window = NewWindow();

        window.Invoking(w =>
        {
            w.OnOpen();
            w.OnOpen();
        }).Should().NotThrow();
    }
}
