using DustBowlGames.DiscordConduit.Core.Api.Models;

namespace DustBowlGames.DiscordConduit.Core.Api.Endpoints;

/// <summary>
/// Provides methods for interacting with Discord message endpoints.
/// </summary>
public sealed class MessageEndpoints
{
    private readonly DiscordRestClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageEndpoints"/> class.
    /// </summary>
    /// <param name="client">The Discord REST client.</param>
    public MessageEndpoints(DiscordRestClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets messages from a channel.
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="limit">Maximum number of messages to return (1-100, default 100).</param>
    /// <param name="before">Get messages before this message ID.</param>
    /// <param name="after">Get messages after this message ID (returns oldest first).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of messages.</returns>
    public Task<List<Message>> GetMessagesAsync(string channelId, int limit = 100, string? before = null, string? after = null, CancellationToken ct = default)
    {
        var path = $"/channels/{channelId}/messages?limit={limit}";
        if (before is not null)
        {
            path += $"&before={before}";
        }

        if (after is not null)
        {
            path += $"&after={after}";
        }

        return _client.GetAsync<List<Message>>(path, ct);
    }

    /// <summary>
    /// Gets a single message by ID.
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="messageId">The message's snowflake ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The message.</returns>
    public Task<Message> GetMessageAsync(string channelId, string messageId, CancellationToken ct = default)
    {
        return _client.GetAsync<Message>($"/channels/{channelId}/messages/{messageId}", ct);
    }

    /// <summary>
    /// Deletes a single message from a channel.
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="messageId">The message's snowflake ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task DeleteMessageAsync(string channelId, string messageId, CancellationToken ct = default)
    {
        return _client.DeleteAsync($"/channels/{channelId}/messages/{messageId}", ct);
    }

    /// <summary>
    /// Sends a message to a channel.
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="content">The message content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created message.</returns>
    public Task<Message> CreateMessageAsync(string channelId, string content, CancellationToken ct = default)
    {
        return _client.PostJsonAsync<Message>($"/channels/{channelId}/messages", new { content }, ct);
    }

    /// <summary>
    /// Bulk deletes messages from a channel (2-100 messages, must be less than 14 days old).
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="messageIds">A list of message snowflake IDs to delete (2-100).</param>
    /// <param name="ct">Cancellation token.</param>
    public Task BulkDeleteMessagesAsync(string channelId, List<string> messageIds, CancellationToken ct = default)
    {
        return _client.PostJsonNoResponseAsync($"/channels/{channelId}/messages/bulk-delete", new { messages = messageIds }, ct);
    }
}
