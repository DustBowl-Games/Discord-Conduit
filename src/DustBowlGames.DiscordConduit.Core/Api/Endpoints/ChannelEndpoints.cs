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
        var response = await _client.GetAsync<ActiveThreadsResponse>($"/guilds/{guildId}/threads/active", ct);
        return response.Threads;
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
