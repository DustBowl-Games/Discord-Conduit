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

    /// <summary>The sticker format: 1 = PNG, 2 = APNG, 3 = Lottie, 4 = GIF.</summary>
    [JsonPropertyName("format_type")]
    public int FormatType { get; init; }

    /// <summary>
    /// The CDN image URL for this sticker, or <c>null</c> for Lottie (format 3) stickers, which are
    /// vector animations with no static image. Webhooks can't send stickers, so a migration posts
    /// this URL instead (Discord embeds it as an image).
    /// </summary>
    [JsonIgnore]
    public string? ImageUrl => FormatType switch
    {
        1 or 2 => $"https://media.discordapp.net/stickers/{Id}.png", // PNG / APNG
        4 => $"https://media.discordapp.net/stickers/{Id}.gif",      // GIF
        _ => null,                                                    // 3 = Lottie (no static image)
    };
}
