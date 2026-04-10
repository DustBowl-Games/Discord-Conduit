using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a file attached to a Discord message.
/// </summary>
public sealed class Attachment
{
    /// <summary>The attachment's snowflake ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The filename of the attachment.</summary>
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    /// <summary>The size of the attachment in bytes.</summary>
    [JsonPropertyName("size")]
    public required long Size { get; init; }

    /// <summary>The source URL of the attachment.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>The proxied URL of the attachment.</summary>
    [JsonPropertyName("proxy_url")]
    public string? ProxyUrl { get; init; }

    /// <summary>The content type of the attachment.</summary>
    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }

    /// <summary>Height of the attachment if it is an image.</summary>
    [JsonPropertyName("height")]
    public int? Height { get; init; }

    /// <summary>Width of the attachment if it is an image.</summary>
    [JsonPropertyName("width")]
    public int? Width { get; init; }

    /// <summary>8 MB — the maximum file size a bot can upload.</summary>
    public const long MaxBotUploadSize = 8 * 1024 * 1024;

    /// <summary>Whether this attachment exceeds the bot upload size limit.</summary>
    [JsonIgnore]
    public bool IsOversized => Size > MaxBotUploadSize;
}
