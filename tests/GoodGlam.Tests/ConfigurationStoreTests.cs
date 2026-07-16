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
            PerSlotThresholds = true,
            Slots = { [GlamSlot.Weapon.Key] = new SlotSetting { Enabled = false, LovesThreshold = 42 } },
            Filters = new PopularityFilters { Job = "tank", ExcludeSeasonal = true, Races = { "race-1" } },
        };

        new ConfigurationStore(this.path).Save(config);
        var loaded = new ConfigurationStore(this.path).Load();

        loaded.Enabled.Should().BeFalse();
        loaded.ShowLogo.Should().BeFalse();
        loaded.LockLogo.Should().BeTrue();
        loaded.LovesThreshold.Should().Be(250);
        loaded.CacheTtlHours.Should().Be(5);
        loaded.PerSlotThresholds.Should().BeTrue();
        loaded.Slots.Should().ContainKey(GlamSlot.Weapon.Key);
        loaded.Slots[GlamSlot.Weapon.Key].Enabled.Should().BeFalse();
        loaded.Slots[GlamSlot.Weapon.Key].LovesThreshold.Should().Be(42);
        loaded.Filters.Job.Should().Be("tank");
        loaded.Filters.ExcludeSeasonal.Should().BeTrue();
        loaded.Filters.Races.Should().ContainSingle().Which.Should().Be("race-1");
    }

    [Fact]
    public void Older_config_without_slot_fields_loads_as_ignore_nothing()
    {
        // A pre-feature config.json has no PerSlotThresholds/Slots fields; it must load as the
        // back-compatible "every slot enabled, single master threshold" behaviour.
        File.WriteAllText(this.path, """{ "Version": 3, "Enabled": true, "LovesThreshold": 150 }""");

        var loaded = new ConfigurationStore(this.path).Load();

        loaded.LovesThreshold.Should().Be(150);
        loaded.PerSlotThresholds.Should().BeFalse();
        loaded.Slots.Should().NotBeNull().And.BeEmpty();
        GlamSlot.All.Should().OnlyContain(slot => loaded.IsSlotEnabled(slot));
    }

    [Fact]
    public void Existing_saved_threshold_is_not_replaced_by_the_new_default()
    {
        File.WriteAllText(this.path, """{ "Version": 3, "LovesThreshold": 100 }""");

        new ConfigurationStore(this.path).Load().LovesThreshold.Should().Be(100);
    }

    [Fact]
    public void Null_slots_field_loads_as_empty()
    {
        File.WriteAllText(this.path, """{ "Version": 3, "Slots": null }""");

        new ConfigurationStore(this.path).Load().Slots.Should().NotBeNull().And.BeEmpty();
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
