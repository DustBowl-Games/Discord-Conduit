using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a Discord poll attached to a message. When migrating, a re-posted poll is created
/// fresh (votes and original timing cannot be carried over via the API), preserving the question,
/// answers, and whether multiple answers are allowed.
/// </summary>
public sealed class Poll
{
    /// <summary>The poll question.</summary>
    [JsonPropertyName("question")]
    public PollMedia? Question { get; init; }

    /// <summary>The available answers.</summary>
    [JsonPropertyName("answers")]
    public List<PollAnswer>? Answers { get; init; }

    /// <summary>When the poll ends (ISO 8601), or <c>null</c> if it has no expiry.</summary>
    [JsonPropertyName("expiry")]
    public string? Expiry { get; init; }

    /// <summary>Whether the poll allows selecting multiple answers.</summary>
    [JsonPropertyName("allow_multiselect")]
    public bool AllowMultiselect { get; init; }

    /// <summary>The poll layout type (1 = default).</summary>
    [JsonPropertyName("layout_type")]
    public int LayoutType { get; init; } = 1;

    /// <summary>Maximum poll duration Discord allows, in hours (32 days).</summary>
    public const int MaxDurationHours = 32 * 24;

    /// <summary>
    /// Builds a poll-create request object suitable for a webhook execute / message create body,
    /// derived from this (already-sent) poll. The duration is taken from the remaining time until
    /// <see cref="Expiry"/> (clamped to Discord's 1–768 hour range), defaulting to 24 hours when
    /// the expiry is missing or already past.
    /// </summary>
    /// <returns>An anonymous object ready to serialize as the <c>poll</c> field, or <c>null</c> if
    /// the poll has no question or answers to reproduce.</returns>
    public object? ToCreateRequest()
    {
        if (Question?.Text is null || Answers is not { Count: > 0 })
            return null;

        var durationHours = 24;
        if (Expiry is not null && DateTimeOffset.TryParse(Expiry, out var expiry))
        {
            var remaining = (expiry - DateTimeOffset.UtcNow).TotalHours;
            if (remaining >= 1)
                durationHours = (int)Math.Min(remaining, MaxDurationHours);
        }

        return new
        {
            question = new { text = Question.Text },
            answers = Answers.Select(a => new
            {
                poll_media = new
                {
                    text = a.PollMedia?.Text,
                    emoji = a.PollMedia?.Emoji is { } e ? new { id = e.Id, name = e.Name } : null,
                },
            }).ToArray(),
            duration = durationHours,
            allow_multiselect = AllowMultiselect,
            layout_type = LayoutType == 0 ? 1 : LayoutType,
        };
    }
}

/// <summary>Text and optional emoji shown for a poll question or answer.</summary>
public sealed class PollMedia
{
    /// <summary>The display text.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>An optional emoji.</summary>
    [JsonPropertyName("emoji")]
    public PollEmoji? Emoji { get; init; }
}

/// <summary>A single poll answer.</summary>
public sealed class PollAnswer
{
    /// <summary>The answer's ID (assigned by Discord; response-only).</summary>
    [JsonPropertyName("answer_id")]
    public int AnswerId { get; init; }

    /// <summary>The answer's text and optional emoji.</summary>
    [JsonPropertyName("poll_media")]
    public PollMedia? PollMedia { get; init; }
}

/// <summary>An emoji reference used in poll media (custom emoji ID or unicode name).</summary>
public sealed class PollEmoji
{
    /// <summary>The custom emoji's snowflake ID, or <c>null</c> for a unicode emoji.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The unicode emoji character, or the custom emoji name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
