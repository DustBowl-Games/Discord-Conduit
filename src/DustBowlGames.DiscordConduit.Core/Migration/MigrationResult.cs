namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// The final result of a completed migration.
/// </summary>
/// <param name="TotalMigrated">The total number of messages successfully migrated.</param>
/// <param name="TotalFailed">The total number of messages that failed to migrate.</param>
/// <param name="TotalSkipped">The total number of messages that were skipped.</param>
/// <param name="Duration">The wall-clock duration of the migration.</param>
/// <param name="FailedMessages">Details about each message that failed to migrate.</param>
public sealed record MigrationResult(
    int TotalMigrated,
    int TotalFailed,
    int TotalSkipped,
    TimeSpan Duration,
    IReadOnlyList<FailedMessage> FailedMessages);

/// <summary>
/// Records a message that could not be migrated.
/// </summary>
/// <param name="SourceMessageId">The snowflake ID of the message in the source channel.</param>
/// <param name="Reason">A human-readable description of why the migration failed.</param>
/// <param name="Timestamp">When the failure occurred.</param>
public sealed record FailedMessage(
    string SourceMessageId,
    string Reason,
    DateTimeOffset Timestamp);
