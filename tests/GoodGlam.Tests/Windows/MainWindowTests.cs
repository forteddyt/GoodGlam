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
    // Seed the no-op log so OnOpen's debug line never dereferences a null Services.Log, matching the
    // sibling window-test classes. The assembly-wide TestBootstrap module initializer already
    // guarantees this, but keeping it here keeps the window tests uniform and self-documenting.
    public MainWindowTests() => TestServices.EnsureLog();

    private static MainWindow NewWindow()
        => new(
            new Configuration { Filters = new() },
            EcFilterCatalog.LoadEmbedded(),
            new NotificationHistoryStore(string.Empty),
            _ => { },
            new DropDetailsWindow());

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
    public void Scroll_regions_are_declared_for_every_tab()
    {
        // The scroll-region map is index-aligned to TabOrder: each tab either owns a child scroll
        // region (its non-null id) or is a deliberate exception (null). Keeping the lengths in lock
        // step guards against adding a tab without deciding how it scrolls.
        MainWindow.TabScrollRegions.Should().HaveSameCount(MainWindow.TabOrder);
    }

    [Fact]
    public void History_is_not_wrapped_in_its_own_scroll_region()
    {
        // History draws a ScrollY table with a frozen header, so it already scrolls beneath the fixed
        // tab bar; wrapping it again would add a redundant double scrollbar. null marks that exception.
        MainWindow.TabScrollRegions[0].Should().BeNull();
    }

    [Fact]
    public void Filters_settings_and_about_each_get_a_distinct_scroll_region()
    {
        var wrapped = MainWindow.TabScrollRegions.Skip(1).ToArray();

        wrapped.Should().OnlyContain(id => !string.IsNullOrWhiteSpace(id));
        wrapped.Should().OnlyHaveUniqueItems();
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
