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

    public void Dispose()
    {
        if (File.Exists(this.path))
            File.Delete(this.path);
    }
}
