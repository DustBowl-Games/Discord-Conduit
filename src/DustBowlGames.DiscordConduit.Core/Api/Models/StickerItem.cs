using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a sticker attached to a message.
/// </summary>
public sealed class StickerItem
{
    /// <summary>The sticker's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Name of the sticker.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}
