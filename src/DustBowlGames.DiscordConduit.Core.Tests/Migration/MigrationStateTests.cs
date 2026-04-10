using DustBowlGames.DiscordConduit.Core.Migration;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

public class MigrationStateTests : IDisposable
{
    private readonly string _tempDir;

    public MigrationStateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "conduit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static MigrationState CreateState(string migrationId = "src-dst-123456")
    {
        return new MigrationState
        {
            MigrationId = migrationId,
            SourceChannelId = "src",
            DestinationChannelId = "dst",
            GuildId = "guild1",
            WebhookId = "wh1",
            WebhookToken = "token1",
            StartedAt = DateTimeOffset.UtcNow,
            TotalMessageCount = 42,
            MigratedCount = 10,
            Phase = MigrationPhase.MigratingMessages,
            Options = new MigrationOptions("src", "dst", "guild1")
        };
    }

    // --- GenerateMigrationId ---

    [Fact]
    public void GenerateMigrationId_ProducesExpectedFormat()
    {
        var id = MigrationState.GenerateMigrationId("111", "222");

        Assert.StartsWith("111-222-", id);
        // The third segment should be a unix timestamp (numeric)
        var parts = id.Split('-');
        Assert.Equal(3, parts.Length);
        Assert.True(long.TryParse(parts[2], out _));
    }

    // --- GetStateFilePath ---

    [Fact]
    public void GetStateFilePath_ProducesCorrectPath()
    {
        var path = MigrationState.GetStateFilePath("/data/app", "my-migration");

        Assert.Equal(Path.Combine("/data/app", "migrations", "my-migration.json"), path);
    }

    // --- SaveAsync / LoadAsync roundtrip ---

    [Fact]
    public async Task SaveAsync_LoadAsync_RoundtripPreservesFields()
    {
        var state = CreateState("roundtrip-test");
        state.MessageIdMap["old1"] = "new1";
        state.MessageIdMap["old2"] = "new2";
        state.FailedMessages.Add(new FailedMessage("fail1", "boom", DateTimeOffset.UtcNow));

        await state.SaveAsync(_tempDir);

        var filePath = MigrationState.GetStateFilePath(_tempDir, "roundtrip-test");
        var loaded = await MigrationState.LoadAsync(filePath);

        Assert.NotNull(loaded);
        Assert.Equal("roundtrip-test", loaded.MigrationId);
        Assert.Equal("src", loaded.SourceChannelId);
        Assert.Equal("dst", loaded.DestinationChannelId);
        Assert.Equal("guild1", loaded.GuildId);
        Assert.Equal("wh1", loaded.WebhookId);
        // WebhookToken is [JsonIgnore] for security — not persisted to disk
        Assert.Equal(string.Empty, loaded.WebhookToken);
        Assert.Equal(42, loaded.TotalMessageCount);
        Assert.Equal(10, loaded.MigratedCount);
        Assert.Equal(MigrationPhase.MigratingMessages, loaded.Phase);
        Assert.Equal(2, loaded.MessageIdMap.Count);
        Assert.Equal("new1", loaded.MessageIdMap["old1"]);
        Assert.Single(loaded.FailedMessages);
        Assert.Equal("fail1", loaded.FailedMessages[0].SourceMessageId);
    }

    // --- LoadAsync non-existent file ---

    [Fact]
    public async Task LoadAsync_NonExistentFile_ReturnsNull()
    {
        var result = await MigrationState.LoadAsync(Path.Combine(_tempDir, "nope.json"));

        Assert.Null(result);
    }

    // --- LoadAllAsync ---

    [Fact]
    public async Task LoadAllAsync_NonExistentDirectory_ReturnsEmptyList()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");

        var result = await MigrationState.LoadAllAsync(missing);

        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAllAsync_WithSavedStates_ReturnsAll()
    {
        var state1 = CreateState("state-1");
        var state2 = CreateState("state-2");

        await state1.SaveAsync(_tempDir);
        await state2.SaveAsync(_tempDir);

        var results = await MigrationState.LoadAllAsync(_tempDir);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, s => s.MigrationId == "state-1");
        Assert.Contains(results, s => s.MigrationId == "state-2");
    }
}
