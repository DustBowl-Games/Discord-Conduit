namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// A preview of what a migration will entail, generated before the migration starts.
/// </summary>
/// <param name="MessageCount">The total number of messages in the source channel.</param>
/// <param name="AttachmentCount">The total number of attachments across all messages.</param>
/// <param name="TotalAttachmentBytes">The combined size of all attachments in bytes.</param>
/// <param name="OversizedAttachments">Attachments that exceed the bot upload size limit.</param>
/// <param name="EstimatedDuration">An estimate of how long the migration will take.</param>
/// <param name="Warnings">Any warnings the user should be aware of before starting.</param>
public sealed record MigrationPreview(
    int MessageCount,
    int AttachmentCount,
    long TotalAttachmentBytes,
    IReadOnlyList<OversizedAttachment> OversizedAttachments,
    TimeSpan EstimatedDuration,
    IReadOnlyList<string> Warnings);
