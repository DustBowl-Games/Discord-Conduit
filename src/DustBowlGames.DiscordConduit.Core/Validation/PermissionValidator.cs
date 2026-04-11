using System.Net;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Validation;

/// <summary>
/// Validates that the bot has the required Discord permissions for a channel migration
/// by probing the relevant API endpoints.
/// </summary>
public sealed class PermissionValidator
{
    private readonly DiscordRestClient _client;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionValidator"/> class.
    /// </summary>
    /// <param name="client">The Discord REST client used to probe permissions.</param>
    /// <param name="logger">Logger instance.</param>
    public PermissionValidator(DiscordRestClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Validates that the bot has the required permissions for migrating messages
    /// from <paramref name="sourceChannelId"/> to <paramref name="destinationChannelId"/>.
    /// </summary>
    /// <param name="sourceChannelId">The source channel's snowflake ID.</param>
    /// <param name="destinationChannelId">The destination channel's snowflake ID.</param>
    /// <param name="guildId">The guild's snowflake ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="PermissionCheckResult"/> describing any permission issues found.</returns>
    public async Task<PermissionCheckResult> ValidateAsync(
        string sourceChannelId,
        string destinationChannelId,
        string guildId,
        CancellationToken ct)
    {
        var issues = new List<PermissionIssue>();

        _logger.Information("Validating bot permissions for migration from {Source} to {Destination} in guild {Guild}",
            sourceChannelId, destinationChannelId, guildId);

        await CheckSourceChannelAsync(sourceChannelId, issues, ct);
        await CheckDestinationChannelAsync(destinationChannelId, issues, ct);

        var result = new PermissionCheckResult(issues.Count == 0, issues);

        if (result.IsValid)
        {
            _logger.Information("Permission validation passed");
        }
        else
        {
            _logger.Warning("Permission validation failed with {Count} issue(s): {Issues}",
                issues.Count, string.Join("; ", issues.Select(i => $"{i.Channel}: {i.Permission}")));
        }

        return result;
    }

    private async Task CheckSourceChannelAsync(string channelId, List<PermissionIssue> issues, CancellationToken ct)
    {
        // Probe READ_MESSAGE_HISTORY by fetching a single message
        try
        {
            await _client.GetAsync<List<Message>>($"/channels/{channelId}/messages?limit=1", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.Debug("403 when reading messages from source channel {ChannelId}", channelId);
            issues.Add(new PermissionIssue(
                "source",
                "READ_MESSAGE_HISTORY",
                "The bot cannot read message history in the source channel. Grant the READ_MESSAGE_HISTORY and VIEW_CHANNEL permissions."));
        }
    }

    private async Task CheckDestinationChannelAsync(string channelId, List<PermissionIssue> issues, CancellationToken ct)
    {
        // Verify the channel is accessible
        try
        {
            await _client.GetAsync<Channel>($"/channels/{channelId}", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.Debug("403 when accessing destination channel {ChannelId}", channelId);
            issues.Add(new PermissionIssue(
                "destination",
                "VIEW_CHANNEL",
                "The bot cannot access the destination channel. Grant the VIEW_CHANNEL permission."));
            // If we can't even view the channel, skip the webhook check
            return;
        }

        // Probe MANAGE_WEBHOOKS by creating and immediately deleting a test webhook.
        // Also probe SEND_MESSAGES by executing the webhook with a test message.
        Webhook? testWebhook = null;
        try
        {
            testWebhook = await _client.PostJsonAsync<Webhook>(
                $"/channels/{channelId}/webhooks",
                new { name = "conduit-permission-check" },
                ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.Debug("403 when creating webhook in destination channel {ChannelId}", channelId);
            issues.Add(new PermissionIssue(
                "destination",
                "MANAGE_WEBHOOKS",
                "The bot cannot create webhooks in the destination channel. Grant the MANAGE_WEBHOOKS permission."));
        }

        // If webhook creation succeeded, test SEND_MESSAGES by executing it
        if (testWebhook is not null && !string.IsNullOrEmpty(testWebhook.Token))
        {
            try
            {
                var testMessage = await _client.PostJsonAsync<Message>(
                    $"/webhooks/{testWebhook.Id}/{testWebhook.Token}?wait=true",
                    new { content = "\u200b" }, // zero-width space — minimal footprint
                    ct);

                // Delete the test message immediately
                try
                {
                    await _client.DeleteAsync(
                        $"/webhooks/{testWebhook.Id}/{testWebhook.Token}/messages/{testMessage.Id}", ct);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to delete test message {MessageId} — manual cleanup may be needed", testMessage.Id);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.Debug("403 when executing webhook in destination channel {ChannelId}", channelId);
                issues.Add(new PermissionIssue(
                    "destination",
                    "SEND_MESSAGES",
                    "The bot's webhook cannot send messages in the destination channel. Grant the SEND_MESSAGES permission."));
            }

            // Clean up the test webhook
            try
            {
                await _client.DeleteAsync($"/webhooks/{testWebhook.Id}", ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete test webhook {WebhookId} — manual cleanup may be needed", testWebhook.Id);
            }
        }
    }
}

/// <summary>
/// The result of a permission validation check.
/// </summary>
/// <param name="IsValid">Whether all required permissions are present.</param>
/// <param name="Issues">The list of permission issues found, empty if <paramref name="IsValid"/> is <c>true</c>.</param>
public sealed record PermissionCheckResult(
    bool IsValid,
    IReadOnlyList<PermissionIssue> Issues);

/// <summary>
/// Describes a single missing permission.
/// </summary>
/// <param name="Channel">Which channel has the issue: "source" or "destination".</param>
/// <param name="Permission">The Discord permission name (e.g. "READ_MESSAGE_HISTORY").</param>
/// <param name="Description">A human-readable explanation of the issue and how to fix it.</param>
public sealed record PermissionIssue(
    string Channel,
    string Permission,
    string Description);
