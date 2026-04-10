using DustBowlGames.DiscordConduit.Core.Api.Models;

namespace DustBowlGames.DiscordConduit.Core.Api.Endpoints;

/// <summary>
/// Provides methods for interacting with Discord guild endpoints.
/// </summary>
public sealed class GuildEndpoints
{
    private readonly DiscordRestClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuildEndpoints"/> class.
    /// </summary>
    /// <param name="client">The Discord REST client.</param>
    public GuildEndpoints(DiscordRestClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the guilds the current bot user is a member of.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of guilds.</returns>
    public Task<List<Guild>> GetCurrentUserGuildsAsync(CancellationToken ct = default)
    {
        return _client.GetAsync<List<Guild>>("/users/@me/guilds", ct);
    }

    /// <summary>
    /// Gets the channels in a guild.
    /// </summary>
    /// <param name="guildId">The guild's snowflake ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of channels in the guild.</returns>
    public Task<List<Channel>> GetGuildChannelsAsync(string guildId, CancellationToken ct = default)
    {
        return _client.GetAsync<List<Channel>>($"/guilds/{guildId}/channels", ct);
    }
}
