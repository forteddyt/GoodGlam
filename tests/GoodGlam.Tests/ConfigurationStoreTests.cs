using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests;

public class ConfigurationStoreTests : IDisposable
{
    private readonly string path;

    public ConfigurationStoreTests()
    {
        TestServices.EnsureLog();
        this.path = Path.Combine(Path.GetTempPath(), $"goodglam-config-{Guid.NewGuid():N}.json");
    }

    [Fact]
    public void Round_trips_all_fields()
    {
        var config = new Configuration
        {
            Enabled = false,
            ShowLogo = false,
            LockLogo = true,
            LovesThreshold = 250,
            CacheTtlHours = 5,
            Filters = new PopularityFilters { Job = "tank", ExcludeSeasonal = true, Races = { "race-1" } },
        };

        new ConfigurationStore(this.path).Save(config);
        var loaded = new ConfigurationStore(this.path).Load();

        loaded.Enabled.Should().BeFalse();
        loaded.ShowLogo.Should().BeFalse();
        loaded.LockLogo.Should().BeTrue();
        loaded.LovesThreshold.Should().Be(250);
        loaded.CacheTtlHours.Should().Be(5);
        loaded.Filters.Job.Should().Be("tank");
        loaded.Filters.ExcludeSeasonal.Should().BeTrue();
        loaded.Filters.Races.Should().ContainSingle().Which.Should().Be("race-1");
    }

    [Fact]
    public void Missing_file_loads_defaults()
    {
        var store = new ConfigurationStore(this.path);

        store.Exists().Should().BeFalse();
        var loaded = store.Load();
        loaded.LovesThreshold.Should().Be(new Configuration().LovesThreshold);
        loaded.Filters.Should().NotBeNull();
    }

    [Fact]
    public void Corrupt_file_loads_defaults()
    {
        File.WriteAllText(this.path, "{ not valid json ]");

        new ConfigurationStore(this.path).Load().LovesThreshold.Should().Be(new Configuration().LovesThreshold);
    }

    [Fact]
    public void Exists_reflects_file_presence()
    {
        var store = new ConfigurationStore(this.path);
        store.Exists().Should().BeFalse();

        store.Save(new Configuration());
        store.Exists().Should().BeTrue();
    }

    [Fact]
    public void FilePath_reports_the_backing_file()
        => new ConfigurationStore(this.path).FilePath.Should().Be(this.path);

    [Fact]
    public void Json_null_literal_loads_defaults()
    {
        // Deserialize returning null (the literal "null") must fall back to a defaults instance.
        File.WriteAllText(this.path, "null");

        var loaded = new ConfigurationStore(this.path).Load();
        loaded.LovesThreshold.Should().Be(new Configuration().LovesThreshold);
        loaded.Filters.Should().NotBeNull();
    }

    [Fact]
    public void Save_swallows_io_errors()
    {
        // Point the store at "<file>/config.json": creating that directory fails because a file of
        // the same name already exists, exercising Save's catch without throwing to the caller.
        var blocker = Path.Combine(Path.GetTempPath(), $"goodglam-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "x");
        try
        {
            var store = new ConfigurationStore(Path.Combine(blocker, "config.json"));
            store.Invoking(s => s.Save(new Configuration())).Should().NotThrow();
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    public void Dispose()
    {
        if (File.Exists(this.path))
            File.Delete(this.path);
    }
}
