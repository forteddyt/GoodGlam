using System.Collections;
using System.Globalization;
using FluentAssertions;
using GoodGlam.Localization;
using Xunit;

namespace GoodGlam.Tests.Localization;

/// <summary>
/// Guards the embedded string catalog: it loads, a missing language fails loudly, every user-facing
/// string is actually present (so a half-authored template can't ship blank UI), the slot-label
/// fallback matches the historical behavior, and the format-string entries carry the placeholder their
/// call sites rely on.
/// </summary>
public class StringCatalogTests
{
    private static readonly StringCatalog Catalog = StringCatalog.LoadEmbedded();

    [Fact]
    public void LoadEmbedded_returns_a_populated_catalog()
    {
        Catalog.Should().NotBeNull();
        Catalog.Common.AppName.Should().Be("GoodGlam");
    }

    [Fact]
    public void LoadEmbedded_throws_for_a_language_that_does_not_ship()
    {
        var act = () => StringCatalog.LoadEmbedded("zz");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Strings.zz.jsonc*");
    }

    [Fact]
    public void Every_user_facing_string_is_present()
    {
        // Walk the whole catalog tree and assert nothing is blank, so a partially-authored template is
        // caught here rather than surfacing as empty labels/tooltips in-game.
        AssertNoBlankStrings(Catalog, nameof(StringCatalog));
    }

    [Theory]
    [InlineData("weapon", "Main Hand")]
    [InlineData("offhand", "Off Hand")]
    [InlineData("earrings", "Ears")]
    [InlineData("ring", "Rings")]
    public void SlotLabel_returns_the_catalog_label_for_a_known_slot(string key, string expected)
        => Catalog.SlotLabel(key).Should().Be(expected);

    [Fact]
    public void SlotLabel_falls_back_to_the_key_for_an_unknown_slot()
        => Catalog.SlotLabel("not-a-slot").Should().Be("not-a-slot");

    [Fact]
    public void Format_strings_are_valid_composite_formats_that_use_their_placeholder()
    {
        // Every entry consumed with string.Format at a call site (HistoryTab, GlamPreview, AboutInfo).
        var formats = new[]
        {
            Catalog.History.DropsLogged,
            Catalog.History.SelectedRankFormat,
            Catalog.GlamPreview.RankFormat,
            Catalog.About.VersionFormat,
            Catalog.About.VersionLocalBuildFormat,
        };

        // Extra args beyond {0} are ignored by string.Format, so a small pool covers every entry.
        var args = Enumerable.Range(0, 4).Cast<object>().ToArray();
        foreach (var format in formats)
        {
            // Must actually reference the value...
            format.Should().Contain("{0}");

            // ...and be a *valid* composite format string. A stray/unbalanced brace or an out-of-range
            // index (trivially introduced by an edit or a future translation) throws FormatException at
            // the ImGui draw call and takes down that tab — catch it here instead of in-game.
            var format1 = format;
            var render = () => string.Format(CultureInfo.InvariantCulture, format1, args);
            render.Should().NotThrow<FormatException>(
                $"'{format1}' is used with string.Format and must be a well-formed composite format string");
        }
    }

    /// <summary>
    /// Recursively asserts every string, string collection, and string-dictionary value under a catalog
    /// node is non-empty, descending into nested catalog types (those in the localization namespace).
    /// </summary>
    private static void AssertNoBlankStrings(object node, string path)
    {
        foreach (var prop in node.GetType().GetProperties())
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;

            var value = prop.GetValue(node);
            var name = $"{path}.{prop.Name}";
            value.Should().NotBeNull($"'{name}' should be populated");

            switch (value)
            {
                case string s:
                    s.Should().NotBeNullOrEmpty($"'{name}' should be a non-empty string");
                    break;

                case IReadOnlyDictionary<string, string> dict:
                    dict.Should().NotBeEmpty($"'{name}' should have entries");
                    foreach (var (key, label) in dict)
                        label.Should().NotBeNullOrEmpty($"'{name}[{key}]' should be a non-empty string");
                    break;

                case IEnumerable<string> list:
                    var items = list.ToList();
                    items.Should().NotBeEmpty($"'{name}' should have entries");
                    items.Should().OnlyContain(item => !string.IsNullOrEmpty(item), $"'{name}' items should be non-empty");
                    break;

                case IEnumerable when value is not string:
                    // No non-string collections are expected in the catalog; ignore defensively.
                    break;

                default:
                    if (value!.GetType().Namespace?.StartsWith("GoodGlam.Localization", StringComparison.Ordinal) == true)
                        AssertNoBlankStrings(value, name);
                    break;
            }
        }
    }
}
