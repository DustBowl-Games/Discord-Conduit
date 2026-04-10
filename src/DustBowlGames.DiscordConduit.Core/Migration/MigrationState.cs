using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Serializable state of a migration, persisted to disk for resume support.
/// </summary>
public sealed class MigrationState
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Schema version of the state file.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Unique identifier for this migration.</summary>
    [JsonPropertyName("migration_id")]
    public required string MigrationId { get; set; }

    /// <summary>The snowflake ID of the source channel.</summary>
    [JsonPropertyName("source_channel_id")]
    public required string SourceChannelId { get; set; }

    /// <summary>The snowflake ID of the destination channel.</summary>
    [JsonPropertyName("destination_channel_id")]
    public required string DestinationChannelId { get; set; }

    /// <summary>The snowflake ID of the guild containing the channels.</summary>
    [JsonPropertyName("guild_id")]
    public required string GuildId { get; set; }

    /// <summary>The snowflake ID of the webhook used for posting messages.</summary>
    [JsonPropertyName("webhook_id")]
    public required string WebhookId { get; set; }

    /// <summary>The token of the webhook used for posting messages.</summary>
    [JsonPropertyName("webhook_token")]
    public required string WebhookToken { get; set; }

    /// <summary>When the migration was started.</summary>
    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the state was last persisted.</summary>
    [JsonPropertyName("last_updated_at")]
    public DateTimeOffset LastUpdatedAt { get; set; }

    /// <summary>The total number of messages in the source channel.</summary>
    [JsonPropertyName("total_message_count")]
    public int TotalMessageCount { get; set; }

    /// <summary>The snowflake ID of the last source message that was successfully migrated, or <c>null</c>.</summary>
    [JsonPropertyName("last_successful_source_message_id")]
    public string? LastSuccessfulSourceMessageId { get; set; }

    /// <summary>The number of messages migrated so far.</summary>
    [JsonPropertyName("migrated_count")]
    public int MigratedCount { get; set; }

    /// <summary>The current phase of the migration.</summary>
    [JsonPropertyName("phase")]
    public MigrationPhase Phase { get; set; }

    /// <summary>Maps original source message IDs to their reposted destination message IDs.</summary>
    [JsonPropertyName("message_id_map")]
    public Dictionary<string, string> MessageIdMap { get; set; } = new();

    /// <summary>Messages that failed to migrate.</summary>
    [JsonPropertyName("failed_messages")]
    public List<FailedMessage> FailedMessages { get; set; } = [];

    /// <summary>The options used for this migration.</summary>
    [JsonPropertyName("options")]
    public required MigrationOptions Options { get; set; }

    /// <summary>
    /// Gets the file path where a migration state file is stored.
    /// </summary>
    /// <param name="appDataPath">The application data directory.</param>
    /// <param name="migrationId">The migration identifier.</param>
    /// <returns>The full path to the state JSON file.</returns>
    public static string GetStateFilePath(string appDataPath, string migrationId)
    {
        return Path.Combine(appDataPath, "migrations", $"{migrationId}.json");
    }

    /// <summary>
    /// Generates a unique migration identifier from source and destination channel IDs.
    /// </summary>
    /// <param name="sourceId">The source channel snowflake ID.</param>
    /// <param name="destId">The destination channel snowflake ID.</param>
    /// <returns>A migration ID in the format <c>{sourceId}-{destId}-{unix_timestamp}</c>.</returns>
    public static string GenerateMigrationId(string sourceId, string destId)
    {
        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{sourceId}-{destId}-{unixTimestamp}";
    }

    /// <summary>
    /// Persists this state to disk as a JSON file.
    /// </summary>
    /// <param name="appDataPath">The application data directory.</param>
    public async Task SaveAsync(string appDataPath)
    {
        LastUpdatedAt = DateTimeOffset.UtcNow;
        var filePath = GetStateFilePath(appDataPath, MigrationId);
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(this, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Loads a migration state from a JSON file.
    /// </summary>
    /// <param name="filePath">The full path to the state file.</param>
    /// <returns>The deserialized state, or <c>null</c> if the file does not exist.</returns>
    public static async Task<MigrationState?> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<MigrationState>(json, JsonOptions);
    }

    /// <summary>
    /// Loads all migration state files from the migrations directory.
    /// </summary>
    /// <param name="appDataPath">The application data directory.</param>
    /// <returns>A list of all persisted migration states.</returns>
    public static async Task<IReadOnlyList<MigrationState>> LoadAllAsync(string appDataPath)
    {
        var migrationsDir = Path.Combine(appDataPath, "migrations");
        if (!Directory.Exists(migrationsDir))
            return [];

        var files = Directory.GetFiles(migrationsDir, "*.json");
        var states = new List<MigrationState>();

        foreach (var file in files)
        {
            var state = await LoadAsync(file);
            if (state is not null)
                states.Add(state);
        }

        return states;
    }
}
