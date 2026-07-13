using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.History;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

public class HistoryTabPreviewInteractionTests
{
    public HistoryTabPreviewInteractionTests() => TestServices.EnsureLog();

    [Fact]
    public void Navigation_persists_the_next_rank_when_it_exists()
    {
        var record = Record(selectedIndex: 0, rowId: Guid.Parse("11111111-1111-1111-1111-111111111111"), imageUrls: ImageUrls(3));
        var calls = new List<(Guid RowId, int SelectedIndex)>();

        var changed = GlamSelectionNavigator.TryMove(
            record,
            GlamSelectionDirection.Next,
            (rowId, selectedIndex) =>
            {
                calls.Add((rowId, selectedIndex));
                return true;
            });

        changed.Should().BeTrue();
        calls.Should().ContainSingle();
        calls[0].RowId.Should().Be(record.RowId);
        calls[0].SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void Navigation_is_a_boundary_no_op_for_the_first_last_and_single_rank()
    {
        var first = Record(selectedIndex: 0, imageUrls: ImageUrls(3));
        var last = Record(selectedIndex: 2, imageUrls: ImageUrls(3));
        var single = Record(selectedIndex: 0, imageUrls: ImageUrls(1));
        var calls = new List<(Guid RowId, int SelectedIndex)>();

        GlamSelectionNavigator.TryMove(first, GlamSelectionDirection.Previous, Capture).Should().BeFalse();
        GlamSelectionNavigator.TryMove(last, GlamSelectionDirection.Next, Capture).Should().BeFalse();
        GlamSelectionNavigator.TryMove(single, GlamSelectionDirection.Next, Capture).Should().BeFalse();

        calls.Should().BeEmpty();
        return;

        bool Capture(Guid rowId, int selectedIndex)
        {
            calls.Add((rowId, selectedIndex));
            return true;
        }
    }

    [Fact]
    public void Navigation_reports_failure_when_the_store_rejects_the_change()
    {
        var record = Record(selectedIndex: 0, imageUrls: ImageUrls(2));
        var calls = new List<int>();

        var changed = GlamSelectionNavigator.TryMove(
            record,
            GlamSelectionDirection.Next,
            (_, selectedIndex) =>
            {
                calls.Add(selectedIndex);
                return false;
            });

        changed.Should().BeFalse();
        calls.Should().Equal(1);
    }

    [Fact]
    public void Preload_policy_queues_the_first_five_ranks_from_rank_one()
    {
        var record = Record(selectedIndex: 0, imageUrls: ImageUrls(10));

        GlamPreviewPreloadPolicy.BuildInitialBatch(record).Should().Equal(
            Url(1),
            Url(2),
            Url(3),
            Url(4),
            Url(5));
    }

    [Fact]
    public void Initial_preload_policy_matches_the_persisted_rank_examples()
    {
        GlamPreviewPreloadPolicy.BuildInitialBatch(Record(selectedIndex: 4, imageUrls: ImageUrls(10))).Should().Equal(
            Url(5),
            Url(1),
            Url(2),
            Url(3),
            Url(4),
            Url(6),
            Url(7),
            Url(8),
            Url(9),
            Url(10));

        GlamPreviewPreloadPolicy.BuildInitialBatch(Record(selectedIndex: 6, imageUrls: ImageUrls(10))).Should().Equal(
            Url(7),
            Url(2),
            Url(3),
            Url(4),
            Url(5),
            Url(6),
            Url(8),
            Url(9),
            Url(10));

        GlamPreviewPreloadPolicy.BuildInitialBatch(Record(selectedIndex: 9, imageUrls: ImageUrls(10))).Should().Equal(
            Url(10),
            Url(5),
            Url(6),
            Url(7),
            Url(8),
            Url(9));
    }

    [Fact]
    public void Initial_preload_policy_skips_missing_images()
    {
        GlamPreviewPreloadPolicy.BuildInitialBatch(
            Record(selectedIndex: 6, imageUrls: ImageUrls(10, missingRanks: [3, 9]))).Should().Equal(
            Url(7),
            Url(2),
            Url(4),
            Url(5),
            Url(6),
            Url(8),
            Url(10));
    }

    [Fact]
    public void Selection_change_preload_exposes_only_the_new_directional_edge()
    {
        var ranks = ImageUrls(10);

        GlamPreviewPreloadPolicy.BuildSelectionChangeBatch(
            Record(selectedIndex: 1, imageUrls: ranks),
            previousIndex: 0).Should().Equal(Url(2), Url(6));
        GlamPreviewPreloadPolicy.BuildSelectionChangeBatch(
            Record(selectedIndex: 2, imageUrls: ranks),
            previousIndex: 1).Should().Equal(Url(3), Url(7));
        GlamPreviewPreloadPolicy.BuildSelectionChangeBatch(
            Record(selectedIndex: 8, imageUrls: ranks),
            previousIndex: 9).Should().Equal(Url(9), Url(4));
        GlamPreviewPreloadPolicy.BuildSelectionChangeBatch(
            Record(selectedIndex: 7, imageUrls: ranks),
            previousIndex: 8).Should().Equal(Url(8), Url(3));
    }

    [Fact]
    public void Hover_state_submits_on_entry_selection_change_and_reentry_but_not_every_frame()
    {
        var state = new GlamPreviewHoverState();
        var rowA = Record(selectedIndex: 0, rowId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), imageUrls: ImageUrls(10));
        var rowANext = rowA with { SelectedIndex = 1 };
        var rowB = Record(selectedIndex: 0, rowId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), imageUrls: ImageUrls(5, prefix: "300"));

        state.OnHover(rowA).Should().Equal(Url(1), Url(2), Url(3), Url(4), Url(5));
        state.EndFrame();

        state.OnHover(rowA).Should().BeEmpty();
        state.EndFrame();

        state.EndFrame();

        state.OnHover(rowA).Should().Equal(Url(1), Url(2), Url(3), Url(4), Url(5));
        state.EndFrame();

        state.OnHover(rowANext).Should().Equal(Url(2), Url(6));
        state.EndFrame();

        state.OnHover(rowB).Should().Equal(
            Url(1, "300"),
            Url(2, "300"),
            Url(3, "300"),
            Url(4, "300"),
            Url(5, "300"));
    }

    private static PopularDropRecord Record(int selectedIndex = 0, Guid? rowId = null, params string?[] imageUrls)
        => new(
            itemId: 1,
            itemName: "Item",
            slot: "body",
            rankedGlams: imageUrls.Select((imageUrl, index) => new GlamResult(
                Loves: 500 - index,
                Url: $"https://ec/glamour/{index + 1}",
                Name: $"Rank {index + 1}",
                ImageUrl: imageUrl)).ToArray(),
            droppedAt: DateTimeOffset.UnixEpoch,
            dutyName: "The Aurum Vale",
            listingUrl: "https://ec/glamours?filter=1",
            selectedIndex: selectedIndex,
            rowId: rowId ?? Guid.NewGuid());

    private static string[] ImageUrls(int count, int[]? missingRanks = null, string prefix = "200")
    {
        var missing = missingRanks ?? [];
        return Enumerable.Range(1, count)
            .Select(rank => missing.Contains(rank) ? null : Url(rank, prefix))
            .ToArray()!;
    }

    private static string Url(int rank, string prefix = "200")
        => $"https://glamours.ec/{prefix}/cover-rank-{rank}.png";
}
