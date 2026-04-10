using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a Discord guild (server).
/// </summary>
public sealed class Guild
{
    /// <summary>The guild's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The guild's name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The guild's icon hash.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>Gets the guild icon CDN URL.</summary>
    public string? GetIconUrl(int size = 128)
    {
        if (Icon is null) return null;
        var ext = Icon.StartsWith("a_") ? "gif" : "png";
        return $"https://cdn.discordapp.com/icons/{Id}/{Icon}.{ext}?size={size}";
    }
}
