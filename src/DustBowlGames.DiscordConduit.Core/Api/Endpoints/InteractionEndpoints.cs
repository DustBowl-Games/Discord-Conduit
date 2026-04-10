namespace DustBowlGames.DiscordConduit.Core.Api.Endpoints;

/// <summary>
/// Provides methods for responding to Discord interactions.
/// </summary>
public sealed class InteractionEndpoints
{
    private readonly DiscordRestClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="InteractionEndpoints"/> class.
    /// </summary>
    /// <param name="client">The Discord REST client.</param>
    public InteractionEndpoints(DiscordRestClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Sends an initial response to an interaction.
    /// </summary>
    /// <param name="interactionId">The interaction's snowflake ID.</param>
    /// <param name="interactionToken">The interaction token.</param>
    /// <param name="response">The interaction response object (e.g., type 4 with data).</param>
    /// <param name="ct">Cancellation token.</param>
    public Task RespondAsync(string interactionId, string interactionToken, object response, CancellationToken ct = default)
    {
        return _client.PostJsonNoResponseAsync($"/interactions/{interactionId}/{interactionToken}/callback", response, ct);
    }

    /// <summary>
    /// Defers the interaction response, showing a "thinking..." indicator to the user.
    /// </summary>
    /// <param name="interactionId">The interaction's snowflake ID.</param>
    /// <param name="interactionToken">The interaction token.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task DeferAsync(string interactionId, string interactionToken, CancellationToken ct = default)
    {
        return _client.PostJsonNoResponseAsync($"/interactions/{interactionId}/{interactionToken}/callback", new { type = 5 }, ct);
    }

    /// <summary>
    /// Sends a follow-up message after deferring an interaction response.
    /// </summary>
    /// <param name="applicationId">The application's snowflake ID.</param>
    /// <param name="interactionToken">The interaction token.</param>
    /// <param name="body">The message body object.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task PostFollowUpAsync(string applicationId, string interactionToken, object body, CancellationToken ct = default)
    {
        return _client.PostJsonNoResponseAsync($"/webhooks/{applicationId}/{interactionToken}", body, ct);
    }

    /// <summary>
    /// Edits the original deferred interaction response.
    /// </summary>
    /// <param name="applicationId">The application's snowflake ID.</param>
    /// <param name="interactionToken">The interaction token.</param>
    /// <param name="body">The updated message body object.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task EditOriginalAsync(string applicationId, string interactionToken, object body, CancellationToken ct = default)
    {
        return _client.PatchJsonAsync<object>($"/webhooks/{applicationId}/{interactionToken}/messages/@original", body, ct);
    }
}
