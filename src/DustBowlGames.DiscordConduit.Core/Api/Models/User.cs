using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a Discord user.
/// </summary>
public sealed class User
{
    /// <summary>The user's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The user's username (not the display name).</summary>
    [JsonPropertyName("username")]
    public required string Username { get; init; }

    /// <summary>The user's display name, if set.</summary>
    [JsonPropertyName("global_name")]
    public string? GlobalName { get; init; }

    /// <summary>The user's avatar hash, if any.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>The user's 4-digit discriminator (legacy, "0" for new usernames).</summary>
    [JsonPropertyName("discriminator")]
    public string? Discriminator { get; init; }

    /// <summary>Whether the user is a bot account.</summary>
    [JsonPropertyName("bot")]
    public bool? Bot { get; init; }

    /// <summary>Gets the display name, preferring global_name over username.</summary>
    [JsonIgnore]
    public string DisplayName => GlobalName ?? Username;

    /// <summary>Gets the avatar CDN URL, or null if no avatar is set.</summary>
    public string? GetAvatarUrl(int size = 128)
    {
        if (Avatar is null) return null;
        var ext = Avatar.StartsWith("a_") ? "gif" : "png";
        return $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.{ext}?size={size}";
    }
}
