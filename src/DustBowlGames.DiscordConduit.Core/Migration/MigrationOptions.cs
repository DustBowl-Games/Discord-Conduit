namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Options that control how a channel migration is performed.
/// </summary>
/// <param name="SourceChannelId">The snowflake ID of the channel to migrate messages from.</param>
/// <param name="DestinationChannelId">The snowflake ID of the channel to migrate messages to.</param>
/// <param name="GuildId">The snowflake ID of the guild (server) containing the channels.</param>
/// <param name="DryRun">When <c>true</c>, simulates the migration without sending any messages.</param>
/// <param name="IncludeReactions">When <c>true</c>, migrates reactions on each message after sending.</param>
public sealed record MigrationOptions(
    string SourceChannelId,
    string DestinationChannelId,
    string GuildId,
    bool DryRun = false,
    bool IncludeReactions = true);
