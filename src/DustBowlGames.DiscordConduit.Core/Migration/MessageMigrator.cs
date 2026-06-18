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

        // The referenced author name is user-controlled — sanitize it the same way as the webhook
        // username so a bidi-override / zero-width name can't corrupt the quoted reply line.
        var author = SanitizeUsername(referenced.Author.DisplayName);
        if (string.IsNullOrWhiteSpace(author))
            author = "unknown-user";

        // Collapse line endings so a multi-line referenced message stays a single-line quote.
        var preview = (referenced.Content ?? string.Empty).ReplaceLineEndings(" ");

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
    /// <param name="includeStickers">When <c>true</c>, appends image URLs for any PNG/APNG/GIF
    /// stickers (webhooks can't send stickers; Discord embeds the image URL).</param>
    /// <param name="includeTimestamp">When <c>true</c>, appends the original send time as subtext.</param>
    /// <returns>The combined content string for the webhook message.</returns>
    public string? BuildWebhookContent(Message message, string? replyReference, bool includeStickers = false, bool includeTimestamp = false)
    {
        string? result;
        if (replyReference is null)
            result = message.Content;
        else if (string.IsNullOrEmpty(message.Content))
            result = replyReference;
        else
            result = $"{replyReference}\n{message.Content}";

        // Sticker fallback: append the sticker image URL(s) — Discord auto-embeds image links.
        if (includeStickers && message.StickerItems is { Count: > 0 })
        {
            foreach (var sticker in message.StickerItems)
            {
                if (sticker.ImageUrl is not null)
                    result = string.IsNullOrEmpty(result) ? sticker.ImageUrl : $"{result}\n{sticker.ImageUrl}";
            }
        }

        // Original-timestamp footer as Discord subtext (-#) with a relative/absolute timestamp.
        if (includeTimestamp)
        {
            var footer = $"-# \U0001F552 originally sent <t:{message.UnixTimestamp}:f>";
            result = string.IsNullOrEmpty(result) ? footer : $"{result}\n{footer}";
        }

        // Discord webhook content limit is 2000 characters
        if (result?.Length > 2000)
            result = result[..1997] + "...";

        return result;
    }

    /// <summary>
    /// Gets the display name to use as the webhook username for a message. The source user controls
    /// their own display name, so the value is sanitized before use: control characters,
    /// bidirectional-override and zero-width code points are stripped, the result is trimmed and
    /// clamped to Discord's 80-character webhook username limit, and an empty result falls back to
    /// the author's username (then a fixed placeholder).
    /// </summary>
    /// <param name="message">The source message.</param>
    /// <returns>A sanitized webhook username, never empty.</returns>
    public string GetWebhookUsername(Message message)
    {
        var sanitized = SanitizeUsername(message.Author.DisplayName);
        if (!string.IsNullOrWhiteSpace(sanitized))
            return sanitized;

        sanitized = SanitizeUsername(message.Author.Username);
        if (!string.IsNullOrWhiteSpace(sanitized))
            return sanitized;

        return "unknown-user";
    }

    // Discord's webhook username limit.
    private const int MaxWebhookUsernameLength = 80;

    /// <summary>
    /// Strips control, bidirectional-override and zero-width code points from a candidate username,
    /// trims it, and clamps it to Discord's webhook username length limit.
    /// </summary>
    /// <param name="value">The raw, user-controlled name.</param>
    /// <returns>The sanitized name, possibly empty if nothing printable remained.</returns>
    private static string SanitizeUsername(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            // Drop bidirectional-override / directional-isolate / zero-width / BOM code points.
            //   U+200B–U+200F : ZWSP, ZWNJ, ZWJ, LRM, RLM
            //   U+202A–U+202E : LRE, RLE, PDF, LRO, RLO
            //   U+2066–U+2069 : LRI, RLI, FSI, PDI
            //   U+FEFF        : BOM / ZWNBSP
            if (ch is (>= '\u200B' and <= '\u200F')
                or (>= '\u202A' and <= '\u202E')
                or (>= '\u2066' and <= '\u2069')
                or '\uFEFF')
            {
                continue;
            }

            // Drop any control character (also catches CR/LF/tab and other C0/C1 controls).
            if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.Control)
                continue;

            builder.Append(ch);
        }

        var result = builder.ToString().Trim();
        if (result.Length > MaxWebhookUsernameLength)
            result = result[..MaxWebhookUsernameLength];

        return result;
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
