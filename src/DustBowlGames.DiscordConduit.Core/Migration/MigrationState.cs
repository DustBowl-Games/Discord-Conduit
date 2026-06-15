using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Serializable state of a migration, persisted to disk for resume support.
/// </summary>
public sealed class MigrationState
{
    private static JsonSerializerOptions JsonOptions => Core.Json.CoreJsonOptions.Default;

    // A well-formed migration id (also used as a directory name): letters, digits, hyphen,
    // underscore only. Used to guard recursive deletes and loaded-state ids.
    private static readonly System.Text.RegularExpressions.Regex MigrationIdRegex =
        new(@"^[A-Za-z0-9_-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

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

    /// <summary>The token of the webhook used for posting messages. Not persisted to disk for security.</summary>
    [JsonIgnore]
    public string WebhookToken { get; set; } = string.Empty;

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
        // Path-traversal guard: the migration ID becomes a directory name, so only allow safe
        // filename-segment characters (letters, digits, hyphen, underscore) — no '.', '/' or '\'.
        if (!System.Text.RegularExpressions.Regex.IsMatch(migrationId, @"^[A-Za-z0-9_-]+$"))
            throw new ArgumentException($"Invalid migration ID format: {migrationId}", nameof(migrationId));
        return Path.Combine(appDataPath, "migrations", migrationId, "state.json");
    }

    /// <summary>
    /// Generates a unique migration identifier from source and destination channel IDs.
    /// </summary>
    /// <param name="sourceId">The source channel snowflake ID.</param>
    /// <param name="destId">The destination channel snowflake ID.</param>
    /// <returns>A migration ID in the format <c>{sourceId}-{destId}-{unix_timestamp}</c>.</returns>
    public static string GenerateMigrationId(string sourceId, string destId)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        return $"{sourceId}-{destId}-{timestamp}-{suffix}";
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

        // Atomic write: write to temp file then rename, so a crash never leaves a partial file
        var tempPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(this, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
        File.Move(tempPath, filePath, overwrite: true);
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

        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
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

        var states = new List<MigrationState>();

        // Load from new directory-based structure: migrations/{id}/state.json
        foreach (var dir in Directory.GetDirectories(migrationsDir))
        {
            var stateFile = Path.Combine(dir, "state.json");
            var state = await LoadAsync(stateFile).ConfigureAwait(false);
            if (state is null)
                continue;

            // Discard a planted file whose id is malformed (its id drives later writes via
            // GetStateFilePath) or whose id doesn't match the folder it was loaded from.
            if (!MigrationIdRegex.IsMatch(state.MigrationId))
                continue;
            if (!string.Equals(state.MigrationId, Path.GetFileName(dir), StringComparison.Ordinal))
                continue;

            states.Add(state);
        }

        // Also load legacy flat files: migrations/{id}.json
        foreach (var file in Directory.GetFiles(migrationsDir, "*.json"))
        {
            var state = await LoadAsync(file).ConfigureAwait(false);
            if (state is null)
                continue;

            if (!MigrationIdRegex.IsMatch(state.MigrationId))
                continue;

            states.Add(state);
        }

        return states;
    }

    /// <summary>
    /// Deletes migration state files older than the specified age.
    /// </summary>
    /// <param name="appDataPath">The application data directory.</param>
    /// <param name="logger">Logger instance for recording cleanup actions.</param>
    /// <param name="maxAgeDays">The maximum age in days. Files older than this will be deleted. Defaults to 30.</param>
    /// <returns>A task that completes when cleanup is finished.</returns>
    public static Task CleanupOldStatesAsync(string appDataPath, ILogger logger, int maxAgeDays = 30)
    {
        var migrationsDir = Path.Combine(appDataPath, "migrations");
        if (!Directory.Exists(migrationsDir))
            return Task.CompletedTask;

        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

        // Clean up new directory-based structure: migrations/{id}/
        foreach (var dir in Directory.GetDirectories(migrationsDir))
        {
            try
            {
                // Never follow a reparse point/symlink/junction into a recursive delete — that
                // could delete content outside the migrations tree.
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                {
                    logger.Warning("Skipping reparse-point migration directory {DirPath}", dir);
                    continue;
                }

                // Only delete well-formed migration directories. Anything else was not created by
                // us and is not eligible for recursive deletion.
                if (!MigrationIdRegex.IsMatch(Path.GetFileName(dir)))
                {
                    logger.Warning("Skipping non-migration directory {DirPath}", dir);
                    continue;
                }

                var stateFile = Path.Combine(dir, "state.json");
                if (!File.Exists(stateFile))
                    continue;

                var lastWrite = File.GetLastWriteTimeUtc(stateFile);
                if (lastWrite < cutoff)
                {
                    Directory.Delete(dir, recursive: true);
                    logger.Information("Deleted old migration directory {DirPath} (last modified {LastWrite})",
                        dir, lastWrite);
                }
            }
            catch (IOException ex)
            {
                logger.Warning(ex, "Failed to delete migration directory {DirPath}", dir);
            }
        }

        // Clean up legacy flat files: migrations/{id}.json
        foreach (var file in Directory.GetFiles(migrationsDir, "*.json"))
        {
            try
            {
                // Only delete well-formed legacy state files (name without extension is the id).
                if (!MigrationIdRegex.IsMatch(Path.GetFileNameWithoutExtension(file)))
                {
                    logger.Warning("Skipping non-migration state file {FilePath}", file);
                    continue;
                }

                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite < cutoff)
                {
                    File.Delete(file);
                    logger.Information("Deleted old migration state file {FilePath} (last modified {LastWrite})",
                        file, lastWrite);
                }
            }
            catch (IOException ex)
            {
                logger.Warning(ex, "Failed to delete migration state file {FilePath}", file);
            }
        }

        return Task.CompletedTask;
    }
}
