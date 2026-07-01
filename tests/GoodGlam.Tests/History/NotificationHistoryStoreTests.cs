using FluentAssertions;
using GoodGlam.History;
using Xunit;

namespace GoodGlam.Tests.History;

public class NotificationHistoryStoreTests : IDisposable
{
    private readonly string path;

    public NotificationHistoryStoreTests()
    {
        TestServices.EnsureLog();
        this.path = Path.Combine(Path.GetTempPath(), $"goodglam-history-{Guid.NewGuid():N}.json");
    }

    private static PopularDropRecord Record(uint id = 1, int loves = 200) =>
        new(id, $"Item {id}", "body", loves, "Glam", "https://x/glamour/1", DateTimeOffset.UnixEpoch,
            "https://x/glamours?filter=1");

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
    public void Persists_and_reloads_across_instances()
    {
        new NotificationHistoryStore(this.path).Add(Record(7, 333));

        var reloaded = new NotificationHistoryStore(this.path).Snapshot();
        reloaded.Should().ContainSingle();
        reloaded[0].ItemId.Should().Be(7);
        reloaded[0].Loves.Should().Be(333);
        reloaded[0].ListingUrl.Should().Be("https://x/glamours?filter=1");
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
    public void Legacy_entry_without_listing_url_loads_with_null()
    {
        // A record persisted before ListingUrl existed: the field is simply absent from the JSON.
        File.WriteAllText(
            this.path,
            """[{"ItemId":7,"ItemName":"Old","Slot":"body","Loves":150,"GlamName":"G","GlamUrl":"u","Timestamp":"2020-01-01T00:00:00+00:00"}]""");

        var record = new NotificationHistoryStore(this.path).Snapshot().Should().ContainSingle().Subject;
        record.ItemId.Should().Be(7);
        record.GlamUrl.Should().Be("u");
        record.ListingUrl.Should().BeNull();
    }

    [Fact]
    public void Rebind_swaps_file_and_reloads_records()
    {
        var other = Path.Combine(Path.GetTempPath(), $"goodglam-history-{Guid.NewGuid():N}.json");
        try
        {
            new NotificationHistoryStore(other).Add(Record(42, 500));

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

            // The second character's file gets the new record; the first character's is untouched.
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

        // A detached store keeps everything in memory only — nothing is written to disk.
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
            // Drop is captured while character A (this.path) is active...
            var store = new NotificationHistoryStore(this.path);
            var bindingForA = store.CaptureBinding();

            // ...then the player switches to character B before the lookup completes.
            store.Rebind(other);
            store.Add(Record(2)); // B's own drop

            var landedActive = store.AddTo(bindingForA, Record(1)); // A's late drop

            landedActive.Should().BeFalse();
            // A's drop went to A's file; B's live history is untouched (no leak, no glow).
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
            // Fill character A (this.path) to the cap, capture its binding, then switch to B.
            var store = new NotificationHistoryStore(this.path);
            for (uint i = 0; i < NotificationHistoryStore.MaxEntries; i++)
                store.Add(Record(i));
            var bindingForA = store.CaptureBinding();
            store.Rebind(other);

            // A late drop for A is appended directly to A's file, which then prunes back to the cap.
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
        // Character A's history path is unwritable ("<file>/history.json"); after switching to B, a
        // late drop for A hits AppendDirect's catch and is reported as not-active without throwing.
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

            // The record still lives in memory even though persistence failed.
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
