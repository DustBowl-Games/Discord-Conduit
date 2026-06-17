using DustBowlGames.DiscordConduit.Core.Api.Models;

namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Optional criteria that narrow which messages a migration moves. All set criteria must match
/// (logical AND). An all-default filter matches every message.
/// </summary>
/// <param name="AuthorId">When set, only messages from this author snowflake ID match.</param>
/// <param name="Since">When set, only messages sent at or after this instant match.</param>
/// <param name="Until">When set, only messages sent at or before this instant match.</param>
/// <param name="ContentContains">When set, only messages whose content contains this text
/// (case-insensitive) match.</param>
/// <param name="AttachmentsOnly">When <c>true</c>, only messages that have at least one attachment match.</param>
/// <param name="ExcludeBots">When <c>true</c>, messages authored by bots are excluded.</param>
public sealed record MessageFilter(
    string? AuthorId = null,
    DateTimeOffset? Since = null,
    DateTimeOffset? Until = null,
    string? ContentContains = null,
    bool AttachmentsOnly = false,
    bool ExcludeBots = false)
{
    /// <summary>Whether any criterion is set (otherwise the filter is a no-op that matches everything).</summary>
    public bool IsActive =>
        AuthorId is not null || Since is not null || Until is not null ||
        ContentContains is not null || AttachmentsOnly || ExcludeBots;

    /// <summary>
    /// Returns <c>true</c> if the message satisfies every set criterion.
    /// </summary>
    /// <param name="message">The message to test.</param>
    /// <returns><c>true</c> if the message matches; otherwise <c>false</c>.</returns>
    public bool Matches(Message message)
    {
        if (AuthorId is not null && !string.Equals(message.Author.Id, AuthorId, StringComparison.Ordinal))
            return false;

        if (ExcludeBots && message.Author.Bot == true)
            return false;

        if (AttachmentsOnly && message.Attachments is not { Count: > 0 })
            return false;

        if (ContentContains is not null &&
            (message.Content is null ||
             message.Content.IndexOf(ContentContains, StringComparison.OrdinalIgnoreCase) < 0))
            return false;

        if ((Since is not null || Until is not null) &&
            DateTimeOffset.TryParse(message.Timestamp, out var sent))
        {
            if (Since is not null && sent < Since.Value)
                return false;
            if (Until is not null && sent > Until.Value)
                return false;
        }

        return true;
    }
}
