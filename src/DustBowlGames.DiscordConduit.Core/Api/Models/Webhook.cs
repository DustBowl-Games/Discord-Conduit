using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a Discord webhook.
/// </summary>
public sealed class Webhook
{
    /// <summary>The webhook's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The webhook's token.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    /// <summary>The channel ID this webhook posts to.</summary>
    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; init; }

    /// <summary>The guild ID this webhook belongs to.</summary>
    [JsonPropertyName("guild_id")]
    public string? GuildId { get; init; }

    /// <summary>The default name of the webhook.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The type of webhook (1 = Incoming, 2 = Channel Follower, 3 = Application).</summary>
    [JsonPropertyName("type")]
    public int Type { get; init; }
}
