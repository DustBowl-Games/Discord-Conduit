namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Represents an attachment that exceeds the bot upload size limit and cannot be re-uploaded.
/// </summary>
/// <param name="SourceMessageId">The snowflake ID of the message that contains the attachment.</param>
/// <param name="Filename">The original filename of the attachment.</param>
/// <param name="SizeBytes">The size of the attachment in bytes.</param>
public sealed record OversizedAttachment(
    string SourceMessageId,
    string Filename,
    long SizeBytes);
