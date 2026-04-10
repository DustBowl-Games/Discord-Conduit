namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// A snapshot of the current migration progress, suitable for display in a UI.
/// </summary>
/// <param name="Completed">The number of messages successfully migrated so far.</param>
/// <param name="Total">The total number of messages to migrate.</param>
/// <param name="Failed">The number of messages that failed to migrate.</param>
/// <param name="Skipped">The number of messages that were skipped (e.g., non-regular message types).</param>
/// <param name="CurrentMessagePreview">A short preview of the message currently being processed, or <c>null</c>.</param>
/// <param name="Elapsed">How long the migration has been running.</param>
/// <param name="EstimatedRemaining">Estimated time remaining, or <c>null</c> if not yet calculable.</param>
/// <param name="Phase">The current phase of the migration.</param>
public sealed record MigrationProgress(
    int Completed,
    int Total,
    int Failed,
    int Skipped,
    string? CurrentMessagePreview,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining,
    MigrationPhase Phase);

/// <summary>
/// Represents the current phase of a channel migration.
/// </summary>
public enum MigrationPhase
{
    /// <summary>Fetching a preview of the source channel (counting messages, attachments, etc.).</summary>
    FetchingPreview,

    /// <summary>Actively migrating messages from source to destination.</summary>
    MigratingMessages,

    /// <summary>Migrating reactions onto the newly created messages.</summary>
    MigratingReactions,

    /// <summary>The migration has finished.</summary>
    Complete
}
