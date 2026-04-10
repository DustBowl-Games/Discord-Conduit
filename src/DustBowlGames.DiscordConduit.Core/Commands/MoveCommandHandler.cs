using System.Text.Json;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Gateway;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Commands;

/// <summary>
/// Handles all move-related slash commands and context menu commands.
/// Supports: Move Message, Move This &amp; Below, move-range, move-thread.
/// </summary>
public sealed class MoveCommandHandler
{
    private readonly MessageEndpoints _messageEndpoints;
    private readonly WebhookEndpoints _webhookEndpoints;
    private readonly ChannelEndpoints _channelEndpoints;
    private readonly InteractionEndpoints _interactionEndpoints;
    private readonly MessageMigrator _messageMigrator;
    private readonly AttachmentHandler _attachmentHandler;
    private readonly string _applicationId;
    private readonly ILogger _logger;

    /// <summary>Command name for the "Move Message" context menu command.</summary>
    public const string MoveMessageCommand = "Move Message";

    /// <summary>Command name for the "Move This &amp; Below" context menu command.</summary>
    public const string MoveThisBelowCommand = "Move This & Below";

    /// <summary>Command name for the move-range slash command.</summary>
    public const string MoveRangeCommand = "move-range";

    /// <summary>Command name for the move-thread slash command.</summary>
    public const string MoveThreadCommand = "move-thread";

    /// <summary>
    /// Creates a new move command handler.
    /// </summary>
    public MoveCommandHandler(
        MessageEndpoints messageEndpoints,
        WebhookEndpoints webhookEndpoints,
        ChannelEndpoints channelEndpoints,
        InteractionEndpoints interactionEndpoints,
        MessageMigrator messageMigrator,
        AttachmentHandler attachmentHandler,
        string applicationId,
        ILogger logger)
    {
        _messageEndpoints = messageEndpoints;
        _webhookEndpoints = webhookEndpoints;
        _channelEndpoints = channelEndpoints;
        _interactionEndpoints = interactionEndpoints;
        _messageMigrator = messageMigrator;
        _attachmentHandler = attachmentHandler;
        _applicationId = applicationId;
        _logger = logger;
    }

    /// <summary>
    /// Handles an incoming interaction. Dispatches to the correct handler based on command name and type.
    /// </summary>
    public async Task HandleInteractionAsync(InteractionCreateEvent interaction)
    {
        if (interaction.Data is null) return;

        try
        {
            switch (interaction.Data.Name)
            {
                case MoveMessageCommand:
                    await HandleMoveMessageAsync(interaction);
                    break;
                case MoveThisBelowCommand:
                    await HandleMoveThisBelowAsync(interaction);
                    break;
                case MoveRangeCommand:
                    await HandleMoveRangeAsync(interaction);
                    break;
                case MoveThreadCommand:
                    await HandleMoveThreadAsync(interaction);
                    break;
                default:
                    _logger.Warning("Unknown command: {CommandName}", interaction.Data.Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling command {CommandName}", interaction.Data.Name);
            await TryEditOriginalAsync(interaction.Token, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the list of application command definitions to register with Discord.
    /// </summary>
    public static List<ApplicationCommand> GetCommandDefinitions()
    {
        return
        [
            // Context menu commands (type 3 = MESSAGE)
            new ApplicationCommand
            {
                Name = MoveMessageCommand,
                Type = 3,
                Description = string.Empty
            },
            new ApplicationCommand
            {
                Name = MoveThisBelowCommand,
                Type = 3,
                Description = string.Empty
            },
            // Slash commands (type 1 = CHAT_INPUT)
            new ApplicationCommand
            {
                Name = MoveRangeCommand,
                Type = 1,
                Description = "Move a range of messages to another channel",
                Options =
                [
                    new ApplicationCommandOption { Name = "start", Type = 3, Description = "ID of the first message", Required = true },
                    new ApplicationCommandOption { Name = "end", Type = 3, Description = "ID of the last message", Required = true },
                    new ApplicationCommandOption { Name = "destination", Type = 7, Description = "Target channel", Required = true }
                ]
            },
            new ApplicationCommand
            {
                Name = MoveThreadCommand,
                Type = 1,
                Description = "Move a thread or forum post to another channel",
                Options =
                [
                    new ApplicationCommandOption { Name = "thread", Type = 7, Description = "Thread to move", Required = true },
                    new ApplicationCommandOption { Name = "destination", Type = 7, Description = "Target channel", Required = true }
                ]
            }
        ];
    }

    private async Task HandleMoveMessageAsync(InteractionCreateEvent interaction)
    {
        // Context menu: target_id is the message ID, resolved.messages has the message
        var targetId = interaction.Data!.TargetId;
        if (targetId is null || interaction.Data.Resolved?.Messages is null)
        {
            await RespondEphemeralAsync(interaction, "Could not find the target message.");
            return;
        }

        // Defer — moving takes time
        await _interactionEndpoints.DeferAsync(interaction.Id, interaction.Token);

        if (!interaction.Data.Resolved.Messages.TryGetValue(targetId, out var message))
        {
            await EditOriginalAsync(interaction.Token, "Could not resolve the target message.");
            return;
        }

        // For context menu commands, we need the user to specify the destination.
        // Send a channel select component as follow-up.
        // For simplicity in v1: check if there's an option named "destination" (there won't be for context menu).
        // We'll use a channel select component.
        await EditOriginalAsync(interaction.Token,
            "Select a destination channel:",
            channelSelect: true,
            customId: $"move_single:{interaction.ChannelId}:{targetId}");
    }

    private async Task HandleMoveThisBelowAsync(InteractionCreateEvent interaction)
    {
        var targetId = interaction.Data!.TargetId;
        if (targetId is null || interaction.ChannelId is null)
        {
            await RespondEphemeralAsync(interaction, "Could not find the target message.");
            return;
        }

        await _interactionEndpoints.DeferAsync(interaction.Id, interaction.Token);

        await EditOriginalAsync(interaction.Token,
            "Select a destination channel for this message and all below:",
            channelSelect: true,
            customId: $"move_below:{interaction.ChannelId}:{targetId}");
    }

    private async Task HandleMoveRangeAsync(InteractionCreateEvent interaction)
    {
        var options = interaction.Data!.Options;
        if (options is null || interaction.ChannelId is null)
        {
            await RespondEphemeralAsync(interaction, "Missing required options.");
            return;
        }

        var startId = GetOptionString(options, "start");
        var endId = GetOptionString(options, "end");
        var destId = GetOptionString(options, "destination");

        if (startId is null || endId is null || destId is null)
        {
            await RespondEphemeralAsync(interaction, "Missing required options: start, end, and destination.");
            return;
        }

        await _interactionEndpoints.DeferAsync(interaction.Id, interaction.Token);

        var movedCount = await MoveMessageRangeAsync(interaction.ChannelId, startId, endId, destId);
        await EditOriginalAsync(interaction.Token, $"Moved {movedCount} message(s) to <#{destId}>.");
    }

    private async Task HandleMoveThreadAsync(InteractionCreateEvent interaction)
    {
        var options = interaction.Data!.Options;
        if (options is null)
        {
            await RespondEphemeralAsync(interaction, "Missing required options.");
            return;
        }

        var threadId = GetOptionString(options, "thread");
        var destId = GetOptionString(options, "destination");

        if (threadId is null || destId is null)
        {
            await RespondEphemeralAsync(interaction, "Missing required options: thread and destination.");
            return;
        }

        await _interactionEndpoints.DeferAsync(interaction.Id, interaction.Token);

        // Get the source thread info
        var sourceThread = await _channelEndpoints.GetChannelAsync(threadId);
        var threadName = sourceThread.Name ?? "Moved Thread";

        // Create a new thread in the destination channel
        var newThread = await _channelEndpoints.CreateThreadAsync(destId, threadName);

        // Fetch all messages from the source thread
        var messages = await FetchAllMessagesChronologicalAsync(threadId);

        // Move them to the new thread
        var movedCount = await MoveMessagesToDestinationAsync(messages, newThread.Id);

        // Delete originals
        await DeleteMessagesAsync(threadId, messages);

        await EditOriginalAsync(interaction.Token,
            $"Moved {movedCount} message(s) from thread **{threadName}** to <#{newThread.Id}>.");
    }

    /// <summary>
    /// Handles the channel select component interaction for context menu commands.
    /// Call this when receiving a component interaction with a custom_id starting with "move_".
    /// </summary>
    public async Task HandleComponentInteractionAsync(InteractionCreateEvent interaction)
    {
        if (interaction.Data is null) return;

        // Component interactions have custom_id in Data.CustomId
        // and selected values in Data.Values
        // For now, we parse the custom_id to determine the action
        var customId = interaction.Data.CustomId;
        if (customId is null) return;

        var parts = customId.Split(':');
        if (parts.Length < 3) return;

        var action = parts[0];
        var sourceChannelId = parts[1];
        var targetMessageId = parts[2];

        // The selected channel ID comes from the component values
        var destChannelId = interaction.Data.Values?.FirstOrDefault();
        if (destChannelId is null)
        {
            await RespondEphemeralAsync(interaction, "No destination channel selected.");
            return;
        }

        await _interactionEndpoints.DeferAsync(interaction.Id, interaction.Token);

        try
        {
            switch (action)
            {
                case "move_single":
                {
                    var messages = await _messageEndpoints.GetMessagesAsync(sourceChannelId, 1);
                    var message = messages.FirstOrDefault(m => m.Id == targetMessageId);
                    if (message is null)
                    {
                        // Fetch the specific message by getting a small range around it
                        var around = await _messageEndpoints.GetMessagesAsync(sourceChannelId, 1, before: targetMessageId);
                        message = around.FirstOrDefault(m => m.Id == targetMessageId);
                    }

                    if (message is null)
                    {
                        await EditOriginalAsync(interaction.Token, "Could not find the message to move.");
                        return;
                    }

                    await MoveMessagesToDestinationAsync([message], destChannelId);
                    await DeleteMessagesAsync(sourceChannelId, [message]);
                    await EditOriginalAsync(interaction.Token, $"Moved 1 message to <#{destChannelId}>.");
                    break;
                }
                case "move_below":
                {
                    var messages = await FetchMessagesFromAsync(sourceChannelId, targetMessageId);
                    var movedCount = await MoveMessagesToDestinationAsync(messages, destChannelId);
                    await DeleteMessagesAsync(sourceChannelId, messages);
                    await EditOriginalAsync(interaction.Token, $"Moved {movedCount} message(s) to <#{destChannelId}>.");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling component interaction {CustomId}", customId);
            await TryEditOriginalAsync(interaction.Token, $"Error: {ex.Message}");
        }
    }

    private async Task<int> MoveMessageRangeAsync(string sourceChannelId, string startId, string endId, string destChannelId)
    {
        // Fetch messages in the range: get messages after startId, filter up to endId
        var allMessages = new List<Message>();
        string? afterId = startId;

        // First, get the start message itself
        var startMessages = await _messageEndpoints.GetMessagesAsync(sourceChannelId, 100, before: startId);
        // Actually we need to include the start message — fetch around it
        var aroundStart = await _messageEndpoints.GetMessagesAsync(sourceChannelId, 100, after: startId);

        // Simpler approach: fetch all messages, then filter by ID range
        // Discord snowflake IDs are chronologically ordered, so we can compare them
        var messages = await FetchAllMessagesChronologicalAsync(sourceChannelId);
        var rangeMessages = messages
            .Where(m => CompareSnowflakes(m.Id, startId) >= 0 && CompareSnowflakes(m.Id, endId) <= 0)
            .ToList();

        var movedCount = await MoveMessagesToDestinationAsync(rangeMessages, destChannelId);
        await DeleteMessagesAsync(sourceChannelId, rangeMessages);
        return movedCount;
    }

    private async Task<int> MoveMessagesToDestinationAsync(List<Message> messages, string destChannelId)
    {
        if (messages.Count == 0) return 0;

        // Create a webhook in the destination
        var webhook = await _webhookEndpoints.CreateWebhookAsync(destChannelId, "Discord Conduit Move");

        if (string.IsNullOrEmpty(webhook.Token))
        {
            throw new InvalidOperationException("Failed to create webhook — missing token. Check bot permissions.");
        }

        var moved = 0;

        try
        {
            foreach (var message in messages)
            {
                if (!message.IsRegularMessage) continue;

                var content = _messageMigrator.BuildWebhookContent(message, replyReference: null);
                var username = _messageMigrator.GetWebhookUsername(message);
                var avatarUrl = _messageMigrator.GetWebhookAvatarUrl(message);

                // Handle attachments
                var uploadable = message.Attachments?
                    .Where(a => !_attachmentHandler.IsOversized(a))
                    .ToList() ?? [];

                // Collect rich embeds from bot messages
                List<Embed>? richEmbeds = null;
                if (message.Author.Bot == true && message.Embeds is { Count: > 0 })
                {
                    richEmbeds = message.Embeds.Where(e => e.Type is "rich").ToList();
                    if (richEmbeds.Count == 0) richEmbeds = null;
                }

                if (uploadable.Count > 0)
                {
                    var files = new List<(byte[] Data, string Filename, string? ContentType)>();
                    foreach (var attachment in uploadable)
                    {
                        var downloaded = await _attachmentHandler.DownloadAttachmentAsync(attachment, CancellationToken.None);
                        files.Add(downloaded);
                    }

                    await _webhookEndpoints.ExecuteWebhookWithFilesAsync(
                        webhook.Id, webhook.Token!,
                        () => _attachmentHandler.CreateMultipartContent(content, username, avatarUrl, richEmbeds, files),
                        CancellationToken.None);
                }
                else
                {
                    var payload = new WebhookExecutePayload
                    {
                        Content = content,
                        Username = username,
                        AvatarUrl = avatarUrl,
                        Embeds = richEmbeds
                    };

                    await _webhookEndpoints.ExecuteWebhookAsync(
                        webhook.Id, webhook.Token!, payload, CancellationToken.None);
                }

                moved++;
            }
        }
        finally
        {
            // Always clean up the webhook
            try { await _webhookEndpoints.DeleteWebhookAsync(webhook.Id); }
            catch { /* best effort */ }
        }

        return moved;
    }

    private async Task DeleteMessagesAsync(string channelId, List<Message> messages)
    {
        var messageIds = messages.Select(m => m.Id).ToList();

        // Discord bulk delete only works for messages < 14 days old, max 100 at a time
        var fourteenDaysAgo = DateTimeOffset.UtcNow.AddDays(-14);
        var recentIds = new List<string>();
        var oldIds = new List<string>();

        foreach (var msg in messages)
        {
            if (DateTimeOffset.TryParse(msg.Timestamp, out var ts) && ts > fourteenDaysAgo)
                recentIds.Add(msg.Id);
            else
                oldIds.Add(msg.Id);
        }

        // Bulk delete recent messages in batches of 100
        for (var i = 0; i < recentIds.Count; i += 100)
        {
            var batch = recentIds.Skip(i).Take(100).ToList();
            if (batch.Count >= 2)
            {
                try { await _messageEndpoints.BulkDeleteMessagesAsync(channelId, batch); }
                catch (Exception ex) { _logger.Warning(ex, "Bulk delete failed, falling back to individual delete"); }
            }
            else if (batch.Count == 1)
            {
                try { await _messageEndpoints.DeleteMessageAsync(channelId, batch[0]); }
                catch (Exception ex) { _logger.Warning(ex, "Failed to delete message {Id}", batch[0]); }
            }
        }

        // Delete old messages individually
        foreach (var id in oldIds)
        {
            try { await _messageEndpoints.DeleteMessageAsync(channelId, id); }
            catch (Exception ex) { _logger.Warning(ex, "Failed to delete message {Id}", id); }
        }
    }

    private async Task<List<Message>> FetchAllMessagesChronologicalAsync(string channelId)
    {
        var all = new List<Message>();
        string? beforeId = null;

        while (true)
        {
            var batch = await _messageEndpoints.GetMessagesAsync(channelId, 100, before: beforeId);
            if (batch.Count == 0) break;
            all.AddRange(batch);
            beforeId = batch[^1].Id;
            if (batch.Count < 100) break;
        }

        all.Reverse();
        return all;
    }

    private async Task<List<Message>> FetchMessagesFromAsync(string channelId, string fromMessageId)
    {
        // Fetch the target message and all messages after it
        var all = new List<Message>();
        string? afterId = fromMessageId;

        // First get messages including the fromMessageId
        // We need to fetch it specifically since after= is exclusive
        var beforeBatch = await _messageEndpoints.GetMessagesAsync(channelId, 1, before: fromMessageId);
        // Actually, let's fetch with a range that includes the target
        var targetBatch = await _messageEndpoints.GetMessagesAsync(channelId, 100);
        var targetMsg = targetBatch.FirstOrDefault(m => m.Id == fromMessageId);
        if (targetMsg is not null) all.Add(targetMsg);

        // Then fetch everything after
        while (true)
        {
            var batch = await _messageEndpoints.GetMessagesAsync(channelId, 100, after: afterId);
            if (batch.Count == 0) break;
            batch.Reverse(); // Discord returns newest first even with after=
            all.AddRange(batch);
            afterId = all[^1].Id;
            if (batch.Count < 100) break;
        }

        return all;
    }

    private async Task RespondEphemeralAsync(InteractionCreateEvent interaction, string message)
    {
        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 4,
            data = new { content = message, flags = 64 } // 64 = ephemeral
        });
    }

    private async Task EditOriginalAsync(string interactionToken, string content,
        bool channelSelect = false, string? customId = null)
    {
        if (channelSelect && customId is not null)
        {
            await _interactionEndpoints.EditOriginalAsync(_applicationId, interactionToken, new
            {
                content,
                components = new[]
                {
                    new
                    {
                        type = 1, // ActionRow
                        components = new[]
                        {
                            new
                            {
                                type = 8, // Channel select
                                custom_id = customId,
                                placeholder = "Select destination channel",
                                channel_types = new[] { 0, 5, 10, 11, 12, 15 } // text, announcement, threads, forum
                            }
                        }
                    }
                }
            });
        }
        else
        {
            await _interactionEndpoints.EditOriginalAsync(_applicationId, interactionToken, new
            {
                content,
                components = Array.Empty<object>() // Clear any select menus
            });
        }
    }

    private async Task TryEditOriginalAsync(string interactionToken, string content)
    {
        try { await EditOriginalAsync(interactionToken, content); }
        catch (Exception ex) { _logger.Warning(ex, "Failed to edit interaction response"); }
    }

    private static string? GetOptionString(List<InteractionOption> options, string name)
    {
        var option = options.FirstOrDefault(o => o.Name == name);
        return option?.Value?.ToString();
    }

    private static int CompareSnowflakes(string a, string b)
    {
        // Snowflake IDs are chronologically ordered unsigned 64-bit integers
        if (ulong.TryParse(a, out var ua) && ulong.TryParse(b, out var ub))
            return ua.CompareTo(ub);
        return string.Compare(a, b, StringComparison.Ordinal);
    }
}
