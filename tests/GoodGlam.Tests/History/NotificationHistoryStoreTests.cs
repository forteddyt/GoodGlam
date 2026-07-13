using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.History;
using Xunit;

namespace GoodGlam.Tests.History;

public class NotificationHistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset DroppedAt = new(2026, 7, 12, 21, 19, 32, TimeSpan.Zero);
    private const string DutyName = "The Aurum Vale";

    private readonly string path;

    public NotificationHistoryStoreTests()
    {
        TestServices.EnsureLog();
        this.path = Path.Combine(Path.GetTempPath(), $"goodglam-history-{Guid.NewGuid():N}.json");
    }

    private static PopularDropRecord Record(
        uint id = 1,
        IReadOnlyList<GlamResult>? rankedGlams = null,
        int selectedIndex = 0,
        Guid rowId = default)
        => new(
            itemId: id,
            itemName: $"Item {id}",
            slot: "body",
            rankedGlams: rankedGlams ?? [new GlamResult(200, "https://x/glamour/1", "Glam", "https://glamours.x/1/cover-0-9.png")],
            droppedAt: DroppedAt,
            dutyName: DutyName,
            listingUrl: "https://x/glamours?filter=1",
            selectedIndex: selectedIndex,
            rowId: rowId);

    [Fact]
    public void Adds_newest_first()
    {
        var store = new NotificationHistoryStore(this.path);
        store.Add(Record(1));
        store.Add(Record(2));

        var snap = store.Snapshot();
        snap.Should().HaveCount(2);
        snap[0].ItemId.Should().Be(2);
        snap[1].ItemId.Should().Be(1);
    }

    [Fact]
    public void Caps_at_max_entries_dropping_oldest()
    {
        var store = new NotificationHistoryStore(this.path);
        for (uint i = 0; i < NotificationHistoryStore.MaxEntries + 50; i++)
            store.Add(Record(i));

        var snap = store.Snapshot();
        snap.Should().HaveCount(NotificationHistoryStore.MaxEntries);
        snap[0].ItemId.Should().Be(NotificationHistoryStore.MaxEntries + 49);
        snap[^1].ItemId.Should().Be(50);
    }

    [Fact]
    public void Persists_and_reloads_ranked_glams()
    {
        new NotificationHistoryStore(this.path).Add(Record(
            7,
            [
                new GlamResult(333, "https://x/glamour/7", "Winner", "https://glamours.x/7.png"),
                new GlamResult(111, "https://x/glamour/8", "Runner Up"),
            ]));

        var reloaded = new NotificationHistoryStore(this.path).Snapshot().Should().ContainSingle().Subject;
        reloaded.ItemId.Should().Be(7);
        reloaded.RankedGlams.Select(glam => (glam.Loves, glam.Name, glam.ImageUrl)).Should().Equal(
            (333, "Winner", "https://glamours.x/7.png"),
            (111, "Runner Up", null));
        reloaded.ListingUrl.Should().Be("https://x/glamours?filter=1");
        reloaded.DroppedAt.Should().Be(DroppedAt);
        reloaded.DutyName.Should().Be(DutyName);
    }

    [Fact]
    public void Persists_and_reloads_the_selected_index()
    {
        var rowId = Guid.NewGuid();
        var store = new NotificationHistoryStore(this.path);
        store.Add(Record(
            5,
            [
                new GlamResult(300, "https://x/glamour/1", "Top"),
                new GlamResult(200, "https://x/glamour/2", "Selected"),
            ],
            rowId: rowId));

        store.UpdateSelectedIndex(rowId, 1).Should().BeTrue();

        var reloaded = new NotificationHistoryStore(this.path).Snapshot().Should().ContainSingle().Subject;
        reloaded.SelectedIndex.Should().Be(1);
        reloaded.ClampedSelectedIndex.Should().Be(1);
        reloaded.SelectedGlam!.Name.Should().Be("Selected");
    }

    [Fact]
    public void Invalid_selected_index_clamps_current_selection_access()
    {
        new NotificationHistoryStore(this.path).Add(Record(
            9,
            [
                new GlamResult(300, "https://x/glamour/1", "Top"),
                new GlamResult(200, "https://x/glamour/2", "Last"),
            ],
            selectedIndex: 99));

        var reloaded = new NotificationHistoryStore(this.path).Snapshot().Should().ContainSingle().Subject;
        reloaded.SelectedIndex.Should().Be(99);
        reloaded.ClampedSelectedIndex.Should().Be(1);
        reloaded.SelectedGlam!.Name.Should().Be("Last");
        reloaded.Loves.Should().Be(200);
    }

    [Fact]
    public void UpdateSelectedIndex_targets_the_exact_row_when_rows_look_identical()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var glams = new[]
        {
            new GlamResult(300, "https://x/glamour/1", "Top"),
            new GlamResult(200, "https://x/glamour/2", "Alt"),
        };
        var store = new NotificationHistoryStore(this.path);
        store.Add(Record(7, glams, rowId: firstId));
        store.Add(Record(7, glams, rowId: secondId));

        store.UpdateSelectedIndex(firstId, 1).Should().BeTrue();

        var snapshot = store.Snapshot();
        snapshot.Single(record => record.RowId == firstId).SelectedGlam!.Url.Should().Be("https://x/glamour/2");
        snapshot.Single(record => record.RowId == secondId).SelectedGlam!.Url.Should().Be("https://x/glamour/1");
    }

    [Fact]
    public void Clear_empties_and_persists()
    {
        var store = new NotificationHistoryStore(this.path);
        store.Add(Record());
        store.Clear();

        store.Snapshot().Should().BeEmpty();
        new NotificationHistoryStore(this.path).Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Missing_file_starts_empty()
        => new NotificationHistoryStore(this.path).Snapshot().Should().BeEmpty();

    [Fact]
    public void Corrupt_file_starts_empty()
    {
        File.WriteAllText(this.path, "{ not valid json ]");
        new NotificationHistoryStore(this.path).Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Rebind_swaps_file_and_reloads_records()
    {
        var other = Path.Combine(Path.GetTempPath(), $"goodglam-history-{Guid.NewGuid():N}.json");
        try
        {
            new NotificationHistoryStore(other).Add(Record(42));

            var store = new NotificationHistoryStore(this.path);
            store.Add(Record(1));
            store.Rebind(other);

            store.Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(42);
        }
        finally
        {
            if (File.Exists(other))
                File.Delete(other);
        }
    }

    [Fact]
    public void Rebind_isolates_writes_to_the_active_character()
    {
        var other = Path.Combine(Path.GetTempPath(), $"goodglam-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new NotificationHistoryStore(this.path);
            store.Add(Record(1));

            store.Rebind(other);
            store.Add(Record(2));

            new NotificationHistoryStore(other).Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(2);
            new NotificationHistoryStore(this.path).Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(1);
        }
        finally
        {
            if (File.Exists(other))
                File.Delete(other);
        }
    }

    [Fact]
    public void Rebind_to_null_detaches_to_empty_without_persisting()
    {
        var store = new NotificationHistoryStore(this.path);
        store.Add(Record(1));

        store.Rebind(null);
        store.Snapshot().Should().BeEmpty();

        store.Add(Record(9));
        store.Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(9);
        File.Exists(this.path).Should().BeTrue();
        new NotificationHistoryStore(this.path).Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(1);
    }

    [Fact]
    public void AddTo_active_binding_records_and_reports_active()
    {
        var store = new NotificationHistoryStore(this.path);
        var binding = store.CaptureBinding();

        store.AddTo(binding, Record(5)).Should().BeTrue();
        store.Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(5);
    }

    [Fact]
    public void AddTo_detached_origin_is_dropped()
    {
        var store = new NotificationHistoryStore(string.Empty);
        var binding = store.CaptureBinding();

        store.AddTo(binding, Record(5)).Should().BeFalse();
        store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void AddTo_after_character_switch_writes_to_the_origin_not_the_current_character()
    {
        var other = Path.Combine(Path.GetTempPath(), $"goodglam-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new NotificationHistoryStore(this.path);
            var bindingForA = store.CaptureBinding();

            store.Rebind(other);
            store.Add(Record(2));

            var landedActive = store.AddTo(bindingForA, Record(1));

            landedActive.Should().BeFalse();
            new NotificationHistoryStore(this.path).Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(1);
            store.Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(2);
        }
        finally
        {
            if (File.Exists(other))
                File.Delete(other);
        }
    }

    [Fact]
    public void AddTo_active_binding_prunes_to_the_cap()
    {
        var store = new NotificationHistoryStore(this.path);
        var binding = store.CaptureBinding();

        for (uint i = 0; i < MaxEntriesPlus(10); i++)
            store.AddTo(binding, Record(i)).Should().BeTrue();

        store.Snapshot().Should().HaveCount(NotificationHistoryStore.MaxEntries);
    }

    [Fact]
    public void AddTo_after_switch_appends_to_origin_and_prunes_it_to_the_cap()
    {
        var other = Path.Combine(Path.GetTempPath(), $"goodglam-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new NotificationHistoryStore(this.path);
            for (uint i = 0; i < NotificationHistoryStore.MaxEntries; i++)
                store.Add(Record(i));
            var bindingForA = store.CaptureBinding();
            store.Rebind(other);

            store.AddTo(bindingForA, Record(9999)).Should().BeFalse();

            var reloaded = new NotificationHistoryStore(this.path).Snapshot();
            reloaded.Should().HaveCount(NotificationHistoryStore.MaxEntries);
            reloaded[0].ItemId.Should().Be(9999);
        }
        finally
        {
            if (File.Exists(other))
                File.Delete(other);
        }
    }

    [Fact]
    public void AddTo_swallows_io_errors_when_appending_to_a_since_switched_origin()
    {
        var blocker = Path.Combine(Path.GetTempPath(), $"goodglam-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "x");
        var other = Path.Combine(Path.GetTempPath(), $"goodglam-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new NotificationHistoryStore(Path.Combine(blocker, "history.json"));
            var bindingForA = store.CaptureBinding();
            store.Rebind(other);

            store.Invoking(s => s.AddTo(bindingForA, Record(1)).Should().BeFalse()).Should().NotThrow();
        }
        finally
        {
            File.Delete(blocker);
            if (File.Exists(other))
                File.Delete(other);
        }
    }

    [Fact]
    public void Add_swallows_io_errors_when_the_file_cannot_be_written()
    {
        var blocker = Path.Combine(Path.GetTempPath(), $"goodglam-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "x");
        try
        {
            var store = new NotificationHistoryStore(Path.Combine(blocker, "history.json"));
            store.Invoking(s => s.Add(Record(1))).Should().NotThrow();

            store.Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(1);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    private static uint MaxEntriesPlus(uint extra) => (uint)NotificationHistoryStore.MaxEntries + extra;

    public void Dispose()
    {
        if (File.Exists(this.path))
            File.Delete(this.path);
    }
}
