using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.History;
using Xunit;

namespace GoodGlam.Tests;

public class CharacterDataManagerTests : IDisposable
{
    private const ulong CharacterA = 0x1111_2222_3333_4444;
    private const ulong CharacterB = 0x5555_6666_7777_8888;

    private readonly string root;
    private readonly string charactersRoot;

    public CharacterDataManagerTests()
    {
        TestServices.EnsureLog();
        this.root = Path.Combine(Path.GetTempPath(), $"goodglam-chardata-{Guid.NewGuid():N}");
        this.charactersRoot = Path.Combine(this.root, "characters");
        Directory.CreateDirectory(this.root);
    }

    private (CharacterDataManager Manager, Configuration Config, NotificationHistoryStore History, NotificationState Glow) NewManager()
    {
        var config = new Configuration { Filters = new() };
        var history = new NotificationHistoryStore(string.Empty);
        var glow = new NotificationState();
        var manager = new CharacterDataManager(this.charactersRoot, config, history, glow);
        return (manager, config, history, glow);
    }

    private static PopularDropRecord Record(uint id) =>
        new(
            id,
            $"Item {id}",
            "body",
            [new GlamResult(200, "https://x/glamour/1", "Glam")],
            DateTimeOffset.UnixEpoch,
            "The Aurum Vale");

    [Fact]
    public void Activate_isolates_config_and_history_per_character()
    {
        var (manager, config, history, _) = this.NewManager();

        manager.Activate(CharacterA, "Alpha", "Behemoth");
        config.LovesThreshold = 200;
        config.Save();
        history.Add(Record(1));

        manager.Activate(CharacterB, "Beta", "Behemoth");
        config.LovesThreshold.Should().Be(new Configuration().LovesThreshold);
        history.Snapshot().Should().BeEmpty();

        manager.Activate(CharacterA, "Alpha", "Behemoth");
        config.LovesThreshold.Should().Be(200);
        history.Snapshot().Should().ContainSingle().Which.ItemId.Should().Be(1);
    }

    [Fact]
    public void Activate_reports_the_active_content_id()
    {
        var (manager, _, _, _) = this.NewManager();

        manager.IsActive.Should().BeFalse();
        manager.Activate(CharacterA, "Alpha", "Behemoth");

        manager.IsActive.Should().BeTrue();
        manager.ActiveContentId.Should().Be(CharacterA);
    }

    [Fact]
    public void Activate_with_zero_content_id_deactivates()
    {
        var (manager, config, _, _) = this.NewManager();
        manager.Activate(CharacterA, "Alpha", "Behemoth");

        manager.Activate(0, null, null);

        manager.IsActive.Should().BeFalse();
        config.SaveSink.Should().BeNull();
    }

    [Fact]
    public void Activate_writes_meta_json()
    {
        var (manager, _, _, _) = this.NewManager();

        manager.Activate(CharacterA, "Alpha", "Behemoth");

        var metaPath = Path.Combine(this.charactersRoot, CharacterA.ToString("x16"), "meta.json");
        File.Exists(metaPath).Should().BeTrue();
        var json = File.ReadAllText(metaPath);
        json.Should().Contain("Alpha").And.Contain("Behemoth");
    }

    [Fact]
    public void Deactivate_resets_to_defaults_and_disables_saving()
    {
        var (manager, config, history, _) = this.NewManager();
        manager.Activate(CharacterA, "Alpha", "Behemoth");
        config.LovesThreshold = 200;
        config.Save();
        history.Add(Record(1));

        manager.Deactivate();

        config.LovesThreshold.Should().Be(new Configuration().LovesThreshold);
        config.SaveSink.Should().BeNull();
        history.Snapshot().Should().BeEmpty();

        // Title-screen edits are discarded: saving without a character does nothing.
        config.LovesThreshold = 999;
        config.Save();

        // Re-loading character A still has the persisted 200, not the discarded 999.
        var reloaded = new Configuration { Filters = new() };
        var reManager = new CharacterDataManager(
            this.charactersRoot, reloaded, new NotificationHistoryStore(string.Empty), new NotificationState());
        reManager.Activate(CharacterA, "Alpha", "Behemoth");
        reloaded.LovesThreshold.Should().Be(200);
    }

    [Fact]
    public void Deactivate_clears_the_unseen_drop_glow_so_it_cannot_leak_across_characters()
    {
        var (manager, _, _, glow) = this.NewManager();
        manager.Activate(CharacterA, "Alpha", "Behemoth");

        // Character A gets a popular drop and the logo glow is raised, but the window is never opened.
        glow.Raise();
        glow.HasUnseen.Should().BeTrue();

        // Switching away (FF14 logs out to the title screen between characters) must clear the latch
        // so the next character doesn't inherit A's glow for a drop that isn't in their history.
        manager.Deactivate();
        glow.HasUnseen.Should().BeFalse();
    }

    [Fact]
    public void Ignores_pre_per_character_global_data()
    {
        // A leftover global history.json from before the per-character change must not bleed into a
        // character: the manager only ever reads characters/<id>/.
        new NotificationHistoryStore(Path.Combine(this.root, "history.json")).Add(Record(7));

        var (manager, config, history, _) = this.NewManager();
        manager.Activate(CharacterA, "Alpha", "Behemoth");

        config.LovesThreshold.Should().Be(new Configuration().LovesThreshold);
        history.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Activate_swallows_meta_write_errors()
    {
        var (manager, _, _, _) = this.NewManager();

        // Pre-create a directory where meta.json should go, so writing the file fails while the rest
        // of activation (config.json) still succeeds — exercising WriteMeta's catch without throwing.
        var folder = Path.Combine(this.charactersRoot, CharacterA.ToString("x16"));
        Directory.CreateDirectory(Path.Combine(folder, "meta.json"));

        manager.Invoking(m => m.Activate(CharacterA, "Alpha", "Behemoth")).Should().NotThrow();
        manager.IsActive.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
            Directory.Delete(this.root, recursive: true);
    }
}
