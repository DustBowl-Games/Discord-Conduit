namespace DustBowlGames.DiscordConduit.Core.Api.Endpoints;

/// <summary>
/// Provides methods for interacting with Discord application command endpoints.
/// </summary>
public sealed class CommandEndpoints
{
    private readonly DiscordRestClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandEndpoints"/> class.
    /// </summary>
    /// <param name="client">The Discord REST client.</param>
    public CommandEndpoints(DiscordRestClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Bulk overwrites all global application commands, replacing the existing set.
    /// </summary>
    /// <param name="appId">The application's snowflake ID.</param>
    /// <param name="commands">A list of command definition objects to register.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of registered command objects.</returns>
    public Task<List<object>> BulkUpsertGlobalCommandsAsync<T>(string appId, List<T> commands, CancellationToken ct = default)
    {
        return _client.PutJsonAsync<List<object>>($"/applications/{appId}/commands", commands, ct);
    }
}
