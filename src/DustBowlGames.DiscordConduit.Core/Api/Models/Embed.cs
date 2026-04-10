using System.Text.Json.Serialization;

namespace DustBowlGames.DiscordConduit.Core.Api.Models;

/// <summary>
/// Represents a Discord message embed.
/// </summary>
public sealed class Embed
{
    /// <summary>Title of the embed.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Type of embed (rich, image, video, gifv, article, link).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Description of the embed.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>URL of the embed.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>ISO 8601 timestamp of the embed content.</summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    /// <summary>Color code of the embed (decimal integer).</summary>
    [JsonPropertyName("color")]
    public int? Color { get; init; }

    /// <summary>Footer information.</summary>
    [JsonPropertyName("footer")]
    public EmbedFooter? Footer { get; init; }

    /// <summary>Image information.</summary>
    [JsonPropertyName("image")]
    public EmbedMedia? Image { get; init; }

    /// <summary>Thumbnail information.</summary>
    [JsonPropertyName("thumbnail")]
    public EmbedMedia? Thumbnail { get; init; }

    /// <summary>Author information.</summary>
    [JsonPropertyName("author")]
    public EmbedAuthor? Author { get; init; }

    /// <summary>Fields information.</summary>
    [JsonPropertyName("fields")]
    public List<EmbedField>? Fields { get; init; }
}

/// <summary>Embed footer object.</summary>
public sealed class EmbedFooter
{
    /// <summary>Footer text.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>URL of the footer icon.</summary>
    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; init; }
}

/// <summary>Embed media (image/thumbnail) object.</summary>
public sealed class EmbedMedia
{
    /// <summary>Source URL of the media.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>Height of the media in pixels.</summary>
    [JsonPropertyName("height")]
    public int? Height { get; init; }

    /// <summary>Width of the media in pixels.</summary>
    [JsonPropertyName("width")]
    public int? Width { get; init; }
}

/// <summary>Embed author object.</summary>
public sealed class EmbedAuthor
{
    /// <summary>Name of the author.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>URL of the author.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>URL of the author icon.</summary>
    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; init; }
}

/// <summary>Embed field object.</summary>
public sealed class EmbedField
{
    /// <summary>Name of the field.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Value of the field.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>Whether this field should display inline.</summary>
    [JsonPropertyName("inline")]
    public bool? Inline { get; init; }
}
