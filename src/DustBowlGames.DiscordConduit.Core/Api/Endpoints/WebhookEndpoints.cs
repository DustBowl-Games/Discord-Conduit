using System.Text.Json.Serialization;
using DustBowlGames.DiscordConduit.Core.Api.Models;

namespace DustBowlGames.DiscordConduit.Core.Api.Endpoints;

/// <summary>
/// Payload for executing a webhook.
/// </summary>
public sealed class WebhookExecutePayload
{
    /// <summary>The message content (up to 2000 characters).</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>Override the default username of the webhook.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>Override the default avatar URL of the webhook.</summary>
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; init; }

    /// <summary>Embedded rich content (up to 10 embeds).</summary>
    [JsonPropertyName("embeds")]
    public List<Embed>? Embeds { get; init; }
}

/// <summary>
/// Provides methods for interacting with Discord webhook endpoints.
/// </summary>
public sealed class WebhookEndpoints
{
    private readonly DiscordRestClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookEndpoints"/> class.
    /// </summary>
    /// <param name="client">The Discord REST client.</param>
    public WebhookEndpoints(DiscordRestClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets a webhook by its ID.
    /// </summary>
    /// <param name="webhookId">The webhook's snowflake ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The webhook, or throws if not found.</returns>
    public Task<Webhook> GetWebhookAsync(string webhookId, CancellationToken ct = default)
    {
        return _client.GetAsync<Webhook>($"/webhooks/{webhookId}", ct);
    }

    /// <summary>
    /// Creates a new webhook for a channel.
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="name">The name of the webhook.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created webhook.</returns>
    public Task<Webhook> CreateWebhookAsync(string channelId, string name, CancellationToken ct = default)
    {
        return _client.PostJsonAsync<Webhook>($"/channels/{channelId}/webhooks", new { name }, ct);
    }

    /// <summary>
    /// Deletes a webhook by its ID.
    /// </summary>
    /// <param name="webhookId">The webhook's snowflake ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task DeleteWebhookAsync(string webhookId, CancellationToken ct = default)
    {
        return _client.DeleteAsync($"/webhooks/{webhookId}", ct);
    }

    /// <summary>
    /// Executes a webhook, sending a message to its channel.
    /// </summary>
    /// <param name="webhookId">The webhook's snowflake ID.</param>
    /// <param name="webhookToken">The webhook's token.</param>
    /// <param name="payload">The message payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="threadId">Optional thread ID to post into (webhook must be in the thread's parent channel).</param>
    /// <returns>The created message.</returns>
    public Task<Message> ExecuteWebhookAsync(string webhookId, string webhookToken, WebhookExecutePayload payload, CancellationToken ct = default, string? threadId = null)
    {
        var url = $"/webhooks/{webhookId}/{webhookToken}?wait=true";
        if (threadId is not null) url += $"&thread_id={threadId}";
        return _client.PostJsonAsync<Message>(url, payload, ct);
    }

    /// <summary>
    /// Executes a webhook with file attachments using multipart form data.
    /// </summary>
    /// <param name="webhookId">The webhook's snowflake ID.</param>
    /// <param name="webhookToken">The webhook's token.</param>
    /// <param name="contentFactory">Factory that creates fresh multipart content for each attempt (supports 429 retries).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="threadId">Optional thread ID to post into (webhook must be in the thread's parent channel).</param>
    /// <returns>The created message.</returns>
    public Task<Message> ExecuteWebhookWithFilesAsync(string webhookId, string webhookToken, Func<MultipartFormDataContent> contentFactory, CancellationToken ct = default, string? threadId = null)
    {
        var url = $"/webhooks/{webhookId}/{webhookToken}?wait=true";
        if (threadId is not null) url += $"&thread_id={threadId}";
        return _client.PostMultipartAsync<Message>(url, contentFactory, ct);
    }
}
