using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a reaction on a Discord message.
/// </summary>
public sealed class Reaction
{
    /// <summary>Number of times this emoji has been used to react.</summary>
    [JsonPropertyName("count")]
    public required int Count { get; init; }

    /// <summary>Whether the current user reacted using this emoji.</summary>
    [JsonPropertyName("me")]
    public bool Me { get; init; }

    /// <summary>The emoji used for the reaction.</summary>
    [JsonPropertyName("emoji")]
    public required Emoji Emoji { get; init; }
}

/// <summary>
/// Represents a Discord emoji.
/// </summary>
public sealed class Emoji
{
    /// <summary>The emoji's snowflake ID (null for unicode emoji).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The emoji name (unicode character for standard emoji, custom emoji name for guild emoji).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Whether this emoji is animated.</summary>
    [JsonPropertyName("animated")]
    public bool? Animated { get; init; }

    /// <summary>Gets the URL-encoded emoji identifier for API calls.</summary>
    [JsonIgnore]
    public string ApiIdentifier => Id is not null ? $"{Name}:{Id}" : Name ?? "";
}
