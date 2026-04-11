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

    /// <summary>Gets the avatar CDN URL for display purposes.</summary>
    public string GetAvatarUrl()
    {
        if (Avatar is not null)
        {
            var ext = Avatar.StartsWith("a_") ? "gif" : "png";
            return $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.{ext}";
        }

        // Default avatar: based on (user_id >> 22) % 6 for new username system,
        // or discriminator % 5 for legacy users
        var index = Discriminator is not null and not "0" && int.TryParse(Discriminator, out var disc)
            ? disc % 5
            : (long.TryParse(Id, out var uid) ? (int)((uid >> 22) % 6) : 0);
        return $"https://cdn.discordapp.com/embed/avatars/{index}.png";
    }

    /// <summary>Gets the avatar URL suitable for webhook avatar_url parameter.
    /// Always uses .png (Discord silently rejects .gif for webhook avatars).</summary>
    public string GetWebhookAvatarUrl()
    {
        if (Avatar is not null)
        {
            // Always use .png — Discord silently rejects .gif for webhook avatar_url
            return $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.png";
        }

        var index = Discriminator is not null and not "0" && int.TryParse(Discriminator, out var disc)
            ? disc % 5
            : (long.TryParse(Id, out var uid) ? (int)((uid >> 22) % 6) : 0);
        return $"https://cdn.discordapp.com/embed/avatars/{index}.png";
    }
}
