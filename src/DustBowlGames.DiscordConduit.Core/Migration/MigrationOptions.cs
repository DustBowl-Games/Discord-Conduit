namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Options that control how a channel migration is performed.
/// </summary>
/// <param name="SourceChannelId">The snowflake ID of the channel to migrate messages from.</param>
/// <param name="DestinationChannelId">The snowflake ID of the channel to migrate messages to.</param>
/// <param name="GuildId">The snowflake ID of the guild (server) containing the channels.</param>
/// <param name="DryRun">When <c>true</c>, simulates the migration without sending any messages.</param>
/// <param name="IncludeReactions">When <c>true</c>, migrates reactions on each message after sending.</param>
/// <param name="IncludePins">When <c>true</c>, re-pins migrated messages that were pinned in the source.</param>
/// <param name="IncludePolls">When <c>true</c>, re-creates polls attached to migrated messages.</param>
/// <param name="IncludeStickers">When <c>true</c>, posts a sticker's image when the message has stickers
/// (webhooks can't send stickers; PNG/APNG/GIF stickers become images, Lottie stickers are skipped).</param>
/// <param name="IncludeTimestamps">When <c>true</c>, appends the original send time as a subtext footer.</param>
/// <param name="Filter">Optional criteria limiting which messages are migrated. Defaults to all messages.</param>
public sealed record MigrationOptions(
    string SourceChannelId,
    string DestinationChannelId,
    string GuildId,
    bool DryRun = false,
    bool IncludeReactions = true,
    bool IncludePins = true,
    bool IncludePolls = true,
    bool IncludeStickers = true,
    bool IncludeTimestamps = false,
    MessageFilter? Filter = null);
