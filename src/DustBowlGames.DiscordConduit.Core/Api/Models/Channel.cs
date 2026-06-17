using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a Discord channel.
/// </summary>
public sealed class Channel
{
    /// <summary>The channel's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The channel type (0 = text, 2 = voice, 4 = category, 5 = announcement, 10 = announcement thread, 11 = public thread, 12 = private thread, 15 = forum).</summary>
    [JsonPropertyName("type")]
    public required int Type { get; init; }

    /// <summary>The channel name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The ID of the parent category or parent channel (for threads).</summary>
    [JsonPropertyName("parent_id")]
    public string? ParentId { get; init; }

    /// <summary>Sorting position of the channel.</summary>
    [JsonPropertyName("position")]
    public int? Position { get; init; }

    /// <summary>The ID of the guild this channel belongs to.</summary>
    [JsonPropertyName("guild_id")]
    public string? GuildId { get; init; }

    /// <summary>The ID of the last message sent in this channel.</summary>
    [JsonPropertyName("last_message_id")]
    public string? LastMessageId { get; init; }

    /// <summary>For forum/media channels, the set of tags that can be applied to posts.</summary>
    [JsonPropertyName("available_tags")]
    public List<ForumTag>? AvailableTags { get; init; }

    /// <summary>For a forum/media post (thread), the IDs of the tags applied to it.</summary>
    [JsonPropertyName("applied_tags")]
    public List<string>? AppliedTags { get; init; }

    /// <summary>Whether this channel is a thread type.</summary>
    [JsonIgnore]
    public bool IsThread => Type is 10 or 11 or 12;

    /// <summary>Whether this channel is a text-based channel that can contain messages.</summary>
    [JsonIgnore]
    public bool IsTextChannel => Type is 0 or 5;

    /// <summary>Whether this channel is a forum or media channel.</summary>
    [JsonIgnore]
    public bool IsForum => Type is 15 or 16;
}

/// <summary>A tag that can be applied to posts in a forum or media channel.</summary>
public sealed class ForumTag
{
    /// <summary>The tag's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The tag's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
