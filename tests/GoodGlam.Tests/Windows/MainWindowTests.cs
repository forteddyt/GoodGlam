using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.History;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// The unified <see cref="MainWindow"/> is almost entirely ImGui (excluded from coverage), but its
/// constructor and <see cref="MainWindow.OnOpen"/> are framework-free: OnOpen arms the History-tab
/// force-select via the pure <see cref="HistoryTabFocus"/>. These cover that non-UI surface.
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
