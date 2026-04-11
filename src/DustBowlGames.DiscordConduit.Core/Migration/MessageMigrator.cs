using DustBowlGames.DiscordConduit.Core.Api.Models;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Handles the logic for preparing a single message for migration via a webhook.
/// </summary>
public sealed class MessageMigrator
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new message migrator.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public MessageMigrator(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds a reply reference string if the message is a reply to another migrated message.
    /// </summary>
    /// <param name="message">The source message.</param>
    /// <param name="messageIdMap">Map of original source message IDs to destination message IDs.</param>
    /// <returns>
    /// A formatted reply reference string like <c>replying to @author: "preview..."</c>,
    /// or <c>null</c> if the message is not a reply or the referenced message is unavailable.
    /// </returns>
    public string? BuildReplyReference(Message message, Dictionary<string, string> messageIdMap)
    {
        if (!message.IsReply || message.ReferencedMessage is null)
            return null;

        var referenced = message.ReferencedMessage;
        var author = referenced.Author.DisplayName;
        var preview = referenced.Content ?? string.Empty;

        if (preview.Length > 100)
            preview = preview[..100] + "...";

        _logger.Debug("Building reply reference for message {MessageId} -> {ReferencedId}",
            message.Id, referenced.Id);

        return $"\u21a9 replying to @{author}: \"{preview}\"";
    }

    /// <summary>
    /// Combines the reply reference (if any) with the original message content for the webhook body.
    /// </summary>
    /// <param name="message">The source message.</param>
    /// <param name="replyReference">The reply reference string, or <c>null</c>.</param>
    /// <returns>The combined content string for the webhook message.</returns>
    public string? BuildWebhookContent(Message message, string? replyReference)
    {
        string? result;
        if (replyReference is null)
            result = message.Content;
        else if (string.IsNullOrEmpty(message.Content))
            result = replyReference;
        else
            result = $"{replyReference}\n{message.Content}";

        // Discord webhook content limit is 2000 characters
        if (result?.Length > 2000)
            result = result[..1997] + "...";

        return result;
    }

    /// <summary>
    /// Gets the display name to use as the webhook username for a message.
    /// </summary>
    /// <param name="message">The source message.</param>
    /// <returns>The author's display name.</returns>
    public string GetWebhookUsername(Message message)
    {
        return message.Author.DisplayName;
    }

    /// <summary>
    /// Gets the avatar CDN URL to use for the webhook message.
    /// </summary>
    /// <param name="message">The source message.</param>
    /// <returns>The author's avatar URL suitable for webhook use.</returns>
    public string GetWebhookAvatarUrl(Message message)
    {
        return message.Author.GetWebhookAvatarUrl();
    }
}
