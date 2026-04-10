using DustBowlGames.DiscordConduit.Core.Api.Models;

namespace DustBowlGames.DiscordConduit.Core.Api.Endpoints;

/// <summary>
/// Provides methods for interacting with Discord reaction endpoints.
/// </summary>
public sealed class ReactionEndpoints
{
    private readonly DiscordRestClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReactionEndpoints"/> class.
    /// </summary>
    /// <param name="client">The Discord REST client.</param>
    public ReactionEndpoints(DiscordRestClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the users that reacted with a specific emoji on a message.
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="messageId">The message's snowflake ID.</param>
    /// <param name="emoji">The emoji (URL-encoded if needed by the caller).</param>
    /// <param name="limit">Maximum number of users to return (1-100, default 100).</param>
    /// <param name="after">Get users after this user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of users who reacted.</returns>
    public Task<List<User>> GetReactionsAsync(string channelId, string messageId, string emoji, int limit = 100, string? after = null, CancellationToken ct = default)
    {
        var encodedEmoji = Uri.EscapeDataString(emoji);
        var path = $"/channels/{channelId}/messages/{messageId}/reactions/{encodedEmoji}?limit={limit}";
        if (after is not null)
        {
            path += $"&after={after}";
        }

        return _client.GetAsync<List<User>>(path, ct);
    }

    /// <summary>
    /// Adds a reaction to a message as the current user.
    /// </summary>
    /// <param name="channelId">The channel's snowflake ID.</param>
    /// <param name="messageId">The message's snowflake ID.</param>
    /// <param name="emoji">The emoji to react with.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task CreateReactionAsync(string channelId, string messageId, string emoji, CancellationToken ct = default)
    {
        var encodedEmoji = Uri.EscapeDataString(emoji);
        return _client.PutAsync($"/channels/{channelId}/messages/{messageId}/reactions/{encodedEmoji}/@me", ct);
    }
}
