using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Handles downloading attachments from Discord CDN URLs and preparing them for re-upload via webhooks.
/// </summary>
public sealed class AttachmentHandler
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates a new attachment handler.
    /// </summary>
    /// <param name="httpClient">An <see cref="HttpClient"/> for downloading CDN files (separate from the Discord API client).</param>
    /// <param name="logger">Logger instance.</param>
    public AttachmentHandler(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Downloads an attachment from the Discord CDN.
    /// </summary>
    /// <param name="attachment">The attachment to download.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple containing the downloaded bytes, filename, and content type.</returns>
    /// <exception cref="HttpRequestException">Thrown if the download fails.</exception>
    public async Task<(byte[] Data, string Filename, string? ContentType)> DownloadAttachmentAsync(
        Attachment attachment, CancellationToken ct)
    {
        _logger.Debug("Downloading attachment {Filename} ({Size} bytes) from {Url}",
            attachment.Filename, attachment.Size, attachment.Url);

        var response = await _httpClient.GetAsync(attachment.Url, ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = attachment.ContentType ?? response.Content.Headers.ContentType?.MediaType;

        _logger.Debug("Downloaded attachment {Filename}: {Bytes} bytes", attachment.Filename, data.Length);

        return (data, attachment.Filename, contentType);
    }

    /// <summary>
    /// Checks whether an attachment exceeds the 8 MB bot upload size limit.
    /// </summary>
    /// <param name="attachment">The attachment to check.</param>
    /// <returns><c>true</c> if the attachment is too large to re-upload; otherwise <c>false</c>.</returns>
    public bool IsOversized(Attachment attachment)
    {
        return attachment.Size > Attachment.MaxBotUploadSize;
    }

    /// <summary>
    /// Builds a <see cref="MultipartFormDataContent"/> for executing a webhook with file attachments.
    /// </summary>
    /// <param name="content">The message text content, or <c>null</c>.</param>
    /// <param name="username">The display name to use for the webhook message, or <c>null</c>.</param>
    /// <param name="avatarUrl">The avatar URL to use for the webhook message, or <c>null</c>.</param>
    /// <param name="embeds">Embeds to include in the message, or <c>null</c>.</param>
    /// <param name="files">The files to attach, each with data, filename, and optional content type.</param>
    /// <returns>A <see cref="MultipartFormDataContent"/> ready for posting to the webhook endpoint.</returns>
    public MultipartFormDataContent CreateMultipartContent(
        string? content,
        string? username,
        string? avatarUrl,
        List<Embed>? embeds,
        List<(byte[] Data, string Filename, string? ContentType)> files)
    {
        var multipart = new MultipartFormDataContent();

        var payload = new Dictionary<string, object?>();

        if (content is not null)
            payload["content"] = content;

        if (username is not null)
            payload["username"] = username;

        if (avatarUrl is not null)
            payload["avatar_url"] = avatarUrl;

        if (embeds is { Count: > 0 })
            payload["embeds"] = embeds;

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        multipart.Add(payloadContent, "payload_json");

        for (var i = 0; i < files.Count; i++)
        {
            var (data, filename, contentType) = files[i];
            var fileContent = new ByteArrayContent(data);

            if (contentType is not null)
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            multipart.Add(fileContent, $"files[{i}]", filename);
        }

        return multipart;
    }
}
