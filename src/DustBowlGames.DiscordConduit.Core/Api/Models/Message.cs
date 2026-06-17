using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a Discord message.
/// </summary>
public sealed class Message
{
    /// <summary>The message's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The ID of the channel the message was sent in.</summary>
    [JsonPropertyName("channel_id")]
    public required string ChannelId { get; init; }

    /// <summary>The author of the message.</summary>
    [JsonPropertyName("author")]
    public required User Author { get; init; }

    /// <summary>The contents of the message.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>When this message was sent (ISO 8601 timestamp).</summary>
    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }

    /// <summary>When this message was last edited, or null.</summary>
    [JsonPropertyName("edited_timestamp")]
    public string? EditedTimestamp { get; init; }

    /// <summary>Any attached files.</summary>
    [JsonPropertyName("attachments")]
    public List<Attachment>? Attachments { get; init; }

    /// <summary>Any embedded content.</summary>
    [JsonPropertyName("embeds")]
    public List<Embed>? Embeds { get; init; }

    /// <summary>Reactions to the message.</summary>
    [JsonPropertyName("reactions")]
    public List<Reaction>? Reactions { get; init; }

    /// <summary>Whether this message is pinned in its channel.</summary>
    [JsonPropertyName("pinned")]
    public bool Pinned { get; init; }

    /// <summary>A poll attached to this message, if any.</summary>
    [JsonPropertyName("poll")]
    public Poll? Poll { get; init; }

    /// <summary>If this message is a reply, the referenced message data.</summary>
    [JsonPropertyName("referenced_message")]
    public Message? ReferencedMessage { get; init; }

    /// <summary>If this message is a reply, the reference metadata.</summary>
    [JsonPropertyName("message_reference")]
    public MessageReference? MessageReference { get; init; }

    /// <summary>Sticker items attached to this message.</summary>
    [JsonPropertyName("sticker_items")]
    public List<StickerItem>? StickerItems { get; init; }

    /// <summary>The type of message (0 = default, 19 = reply, 7 = guild member join, etc.).</summary>
    [JsonPropertyName("type")]
    public int Type { get; init; }

    /// <summary>Whether this is a regular user/bot message (type 0 or 19).</summary>
    [JsonIgnore]
    public bool IsRegularMessage => Type is 0 or 19;

    /// <summary>Whether this message is a reply to another message.</summary>
    [JsonIgnore]
    public bool IsReply => Type == 19 || MessageReference is not null;

    /// <summary>Whether this message has stickers attached.</summary>
    [JsonIgnore]
    public bool HasStickers => StickerItems is { Count: > 0 };

    /// <summary>Gets the Unix timestamp in seconds from the message's ISO 8601 timestamp.</summary>
    [JsonIgnore]
    public long UnixTimestamp =>
        DateTimeOffset.TryParse(Timestamp, out var dto) ? dto.ToUnixTimeSeconds() : 0;
}

/// <summary>
/// Reference data for a reply or crosspost.
/// </summary>
public sealed class MessageReference
{
    /// <summary>ID of the originating message.</summary>
    [JsonPropertyName("message_id")]
    public string? MessageId { get; init; }

    /// <summary>ID of the originating channel.</summary>
    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; init; }

    /// <summary>ID of the originating guild.</summary>
    [JsonPropertyName("guild_id")]
    public string? GuildId { get; init; }
}
