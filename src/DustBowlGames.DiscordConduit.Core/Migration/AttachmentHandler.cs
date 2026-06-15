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

    private static JsonSerializerOptions JsonOptions => Core.Json.CoreJsonOptions.Default;

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

        var uri = new Uri(attachment.Url);
        if (!IsAllowedDiscordCdnHost(uri.Host))
        {
            _logger.Warning("Skipping attachment with non-Discord URL host: {Host}", uri.Host);
            throw new InvalidOperationException($"Attachment URL is not from a Discord CDN host: {uri.Host}");
        }

        // Hard cap on actual downloaded bytes, independent of the self-reported attachment.Size.
        // A small margin above the upload limit is allowed so files right at the boundary still download.
        const long maxDownloadBytes = Attachment.MaxBotUploadSize + (1024 * 1024);

        using var response = await _httpClient.GetAsync(
            attachment.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        // Re-validate the FINAL request URI host. HttpClient follows redirects by default, and the
        // initial allowlist check only covers the original URL — a 3xx redirect could otherwise send
        // the request (and our read) to an arbitrary host. Reject any redirect that left the allowlist.
        var finalHost = response.RequestMessage?.RequestUri?.Host;
        if (finalHost is not null && !IsAllowedDiscordCdnHost(finalHost))
        {
            _logger.Warning("Attachment download redirected to a non-Discord host: {Host}", finalHost);
            throw new InvalidOperationException(
                $"Attachment download redirected off the Discord CDN allowlist to: {finalHost}");
        }

        response.EnsureSuccessStatusCode();

        // Reject early if the server advertises a body larger than the cap.
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > maxDownloadBytes)
        {
            throw new InvalidOperationException(
                $"Attachment {attachment.Filename} reports Content-Length {contentLength} bytes, " +
                $"which exceeds the maximum download size of {maxDownloadBytes} bytes.");
        }

        var contentType = attachment.ContentType ?? response.Content.Headers.ContentType?.MediaType;

        // Stream the body in chunks, enforcing the cap on the running total so a server that
        // lies about (or omits) Content-Length still cannot exhaust memory.
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream(
            contentLength is > 0 and <= maxDownloadBytes ? (int)contentLength.Value : 0);

        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxDownloadBytes)
            {
                throw new InvalidOperationException(
                    $"Attachment {attachment.Filename} exceeds the maximum download size of " +
                    $"{maxDownloadBytes} bytes; download aborted.");
            }

            buffer.Write(chunk, 0, read);
        }

        var data = buffer.ToArray();

        _logger.Debug("Downloaded attachment {Filename}: {Bytes} bytes", attachment.Filename, data.Length);

        return (data, attachment.Filename, contentType);
    }

    /// <summary>Returns true if the host belongs to Discord's CDN allowlist.</summary>
    private static bool IsAllowedDiscordCdnHost(string host) =>
        host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".discordapp.net", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks whether an attachment exceeds the 25 MB bot upload size limit.
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

        // Suppress all mentions to prevent @everyone/@here injection via migrated content
        payload["allowed_mentions"] = new { parse = Array.Empty<string>() };

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        multipart.Add(payloadContent, "payload_json");

        for (var i = 0; i < files.Count; i++)
        {
            var (data, filename, contentType) = files[i];
            var fileContent = new ByteArrayContent(data);

            if (contentType is not null)
            {
                try
                {
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                }
                catch (FormatException)
                {
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                }
            }

            // Sanitize filename to prevent CRLF injection in Content-Disposition header
            var safeFilename = filename.ReplaceLineEndings("_").Replace("\"", "_");
            multipart.Add(fileContent, $"files[{i}]", safeFilename);
        }

        return multipart;
    }
}
