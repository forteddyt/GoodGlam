using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests;

/// <summary>Guards the default values of the logo-related configuration flags.</summary>
public class ConfigurationTests
{
    [Fact]
    public void Popularity_threshold_defaults_to_fifty_loves()
        => new Configuration().LovesThreshold.Should().Be(50);

    [Fact]
    public void Logo_flags_default_to_shown_and_unlocked()
    {
        var config = new Configuration();

        config.ShowLogo.Should().BeTrue();
        config.LockLogo.Should().BeFalse();
    }

    [Fact]
    public void Slot_settings_default_to_ignore_nothing()
    {
        var config = new Configuration();

        config.PerSlotThresholds.Should().BeFalse();
        config.Slots.Should().BeEmpty();
        // With nothing configured, every slot is analysed exactly like before.
        GlamSlot.All.Should().OnlyContain(slot => config.IsSlotEnabled(slot));
    }

    [Fact]
    public void IsSlotEnabled_reflects_the_stored_setting()
    {
        var config = new Configuration();
        config.Slots[GlamSlot.Weapon.Key] = new SlotSetting { Enabled = false };

        config.IsSlotEnabled(GlamSlot.Weapon).Should().BeFalse();
        config.IsSlotEnabled(GlamSlot.Head).Should().BeTrue(); // unconfigured slot stays enabled
    }

    [Fact]
    public void EffectiveThreshold_uses_master_when_per_slot_is_off()
    {
        var config = new Configuration { LovesThreshold = 120, PerSlotThresholds = false };
        config.Slots[GlamSlot.Head.Key] = new SlotSetting { LovesThreshold = 5 };

        // The per-slot override is ignored while the advanced toggle is off.
        config.EffectiveThreshold(GlamSlot.Head).Should().Be(120);
    }

    [Fact]
    public void EffectiveThreshold_uses_the_override_when_per_slot_is_on()
    {
        var config = new Configuration { LovesThreshold = 120, PerSlotThresholds = true };
        config.Slots[GlamSlot.Head.Key] = new SlotSetting { LovesThreshold = 5 };

        config.EffectiveThreshold(GlamSlot.Head).Should().Be(5);
    }

    [Fact]
    public void EffectiveThreshold_falls_back_to_master_when_override_unset()
    {
        // Advanced on, but the slot has no explicit threshold (or none at all): it tracks the master.
        var config = new Configuration { LovesThreshold = 120, PerSlotThresholds = true };
        config.Slots[GlamSlot.Head.Key] = new SlotSetting { Enabled = false }; // no LovesThreshold

        config.EffectiveThreshold(GlamSlot.Head).Should().Be(120);
        config.EffectiveThreshold(GlamSlot.Body).Should().Be(120); // no entry at all
    }

    [Fact]
    public void CopyFrom_adopts_every_persisted_field()
    {
        var live = new Configuration();
        var other = new Configuration
        {
            Version = 99,
            Enabled = false,
            ShowLogo = false,
            LockLogo = true,
            LovesThreshold = 321,
            PerSlotThresholds = true,
            Slots = { [GlamSlot.Ring.Key] = new SlotSetting { Enabled = false, LovesThreshold = 42 } },
            CacheTtlHours = 7,
            Filters = new PopularityFilters { Job = "healer", ExcludeMogstation = true },
        };

        live.CopyFrom(other);

        live.Version.Should().Be(99);
        live.Enabled.Should().BeFalse();
        live.ShowLogo.Should().BeFalse();
        live.LockLogo.Should().BeTrue();
        live.LovesThreshold.Should().Be(321);
        live.PerSlotThresholds.Should().BeTrue();
        live.Slots.Should().ContainKey(GlamSlot.Ring.Key);
        live.Slots[GlamSlot.Ring.Key].Enabled.Should().BeFalse();
        live.Slots[GlamSlot.Ring.Key].LovesThreshold.Should().Be(42);
        live.CacheTtlHours.Should().Be(7);
        live.Filters.Job.Should().Be("healer");
        live.Filters.ExcludeMogstation.Should().BeTrue();
    }

    [Fact]
    public void CopyFrom_substitutes_empty_slots_when_source_is_null()
    {
        var live = new Configuration();
        var other = new Configuration { Slots = null! };

        live.CopyFrom(other);

        live.Slots.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void CopyFrom_substitutes_default_filters_when_source_is_null()
    {
        var live = new Configuration();
        var other = new Configuration { Filters = null! };

        live.CopyFrom(other);

        live.Filters.Should().NotBeNull();
    }

    [Fact]
    public void Save_routes_to_the_sink()
    {
        var config = new Configuration();
        Configuration? saved = null;
        config.SaveSink = c => saved = c;

        config.Save();

        saved.Should().BeSameAs(config);
    }

    [Fact]
    public void Save_is_a_no_op_when_no_sink_is_bound()
    {
        var config = new Configuration { SaveSink = null };

        var act = config.Save;

        act.Should().NotThrow();
    }
}
