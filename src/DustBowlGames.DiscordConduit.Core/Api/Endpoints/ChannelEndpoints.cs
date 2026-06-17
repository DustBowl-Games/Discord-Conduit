using System.Text.Json.Serialization;
using DustBowlGames.DiscordConduit.Core.Api.Models;

namespace DustBowlGames.DiscordConduit.Core.Api.Endpoints;

/// <summary>
/// Provides methods for interacting with Discord channel endpoints.
/// </summary>
public sealed class ChannelEndpoints
{
    private readonly DiscordRestClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelEndpoints"/> class.
    /// </summary>
    /// <param name="client">The Discord REST client.</param>
    public ChannelEndpoints(DiscordRestClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets a channel by its ID.
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The channel.</returns>
    public Task<Channel> GetChannelAsync(string channelId, CancellationToken ct = default)
    {
        return _client.GetAsync<Channel>($"/channels/{channelId}", ct);
    }

    /// <summary>
    /// Gets the active threads in a guild.
    /// </summary>
    /// <param name="guildId">The guild's snowflake ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of active thread channels.</returns>
    public async Task<List<Channel>> GetActiveThreadsAsync(string guildId, CancellationToken ct = default)
    {
        var response = await _client.GetAsync<ActiveThreadsResponse>($"/guilds/{guildId}/threads/active", ct).ConfigureAwait(false);
        return response.Threads;
    }

    /// <summary>
    /// Creates a new public thread in a channel.
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="name">The name of the thread.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created thread channel.</returns>
    public Task<Channel> CreateThreadAsync(string channelId, string name, CancellationToken ct = default)
    {
        return _client.PostJsonAsync<Channel>($"/channels/{channelId}/threads", new { name, type = 11 }, ct);
    }

    /// <summary>
    /// Creates a new forum (or media) post in a forum channel. Unlike a text-channel thread,
    /// Discord requires forum/media threads to be created with an initial message, so a starter
    /// message is posted together with the thread (the migrated messages follow via webhook).
    /// </summary>
    /// <param name="channelId">The forum channel's snowflake ID.</param>
    /// <param name="name">The title of the forum post.</param>
    /// <param name="starterContent">The content of the mandatory starter message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created forum-post thread channel.</returns>
    public Task<Channel> CreateForumPostAsync(string channelId, string name, string starterContent, CancellationToken ct = default)
    {
        return _client.PostJsonAsync<Channel>(
            $"/channels/{channelId}/threads",
            new
            {
                name,
                message = new
                {
                    content = starterContent,
                    allowed_mentions = new { parse = Array.Empty<string>() },
                },
            },
            ct);
    }

    /// <summary>
    /// Internal response wrapper for the active threads endpoint.
    /// </summary>
    internal sealed class ActiveThreadsResponse
    {
        /// <summary>The list of active threads.</summary>
        [JsonPropertyName("threads")]
        public required List<Channel> Threads { get; init; }
    }
}
