using System.Text.Json;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Gateway;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Commands;

/// <summary>
/// Handles all move-related slash commands and context menu commands using a
/// Pippin-style multi-step interaction flow with modals, select menus, and buttons.
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
    private readonly InteractionStateStore _stateStore;
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
    /// <param name="messageEndpoints">Message API endpoints.</param>
    /// <param name="webhookEndpoints">Webhook API endpoints.</param>
    /// <param name="channelEndpoints">Channel API endpoints.</param>
    /// <param name="interactionEndpoints">Interaction API endpoints.</param>
    /// <param name="messageMigrator">Message migration helper.</param>
    /// <param name="attachmentHandler">Attachment download/upload helper.</param>
    /// <param name="stateStore">In-memory session store for multi-step interactions.</param>
    /// <param name="applicationId">The bot's application ID.</param>
    /// <param name="logger">Logger instance.</param>
    public MoveCommandHandler(
        MessageEndpoints messageEndpoints,
        WebhookEndpoints webhookEndpoints,
        ChannelEndpoints channelEndpoints,
        InteractionEndpoints interactionEndpoints,
        MessageMigrator messageMigrator,
        AttachmentHandler attachmentHandler,
        InteractionStateStore stateStore,
        string applicationId,
        ILogger logger)
    {
        _messageEndpoints = messageEndpoints;
        _webhookEndpoints = webhookEndpoints;
        _channelEndpoints = channelEndpoints;
        _interactionEndpoints = interactionEndpoints;
        _messageMigrator = messageMigrator;
        _attachmentHandler = attachmentHandler;
        _stateStore = stateStore;
        _applicationId = applicationId;
        _logger = logger;
    }

    /// <summary>
    /// Returns the list of application command definitions to register with Discord.
    /// </summary>
    /// <returns>A list of application commands.</returns>
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

    // ─────────────────────────────────────────────────────────────────────────
    //  Entry point 1: Application commands (type 2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles an incoming application command interaction (type 2).
    /// Dispatches to the correct handler based on command name.
    /// </summary>
    /// <param name="interaction">The interaction event.</param>
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

    // ─────────────────────────────────────────────────────────────────────────
    //  Entry point 2: Component interactions (type 3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles a message component interaction (type 3) such as buttons and select menus.
    /// Routes by custom_id to the appropriate step in the multi-step flow.
    /// </summary>
    /// <param name="interaction">The interaction event.</param>
    public async Task HandleComponentAsync(InteractionCreateEvent interaction)
    {
        if (interaction.Data is null) return;

        var customId = interaction.Data.CustomId;
        if (customId is null) return;

        try
        {
            switch (customId)
            {
                case "move_action":
                    await HandleActionSelectAsync(interaction);
                    break;
                case "move_dest":
                    await HandleDestinationSelectAsync(interaction);
                    break;
                case "move_yes":
                    await HandleConfirmYesAsync(interaction);
                    break;
                case "move_no":
                    await HandleConfirmNoAsync(interaction);
                    break;
                case "move_delete":
                    await HandleDeleteOriginalsAsync(interaction);
                    break;
                case "move_keep":
                    await HandleKeepOriginalsAsync(interaction);
                    break;
                default:
                    // Handle legacy slash-command delete/keep buttons with session key in custom_id
                    if (customId.StartsWith("move_slash_delete:", StringComparison.Ordinal))
                        await HandleSlashDeleteAsync(interaction, customId);
                    else if (customId.StartsWith("move_slash_keep:", StringComparison.Ordinal))
                        await HandleSlashKeepAsync(interaction, customId);
                    else
                        _logger.Warning("Unknown component custom_id: {CustomId}", customId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling component interaction {CustomId}", customId);
            await TryEditOriginalAsync(interaction.Token, $"Error: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Entry point 3: Modal submissions (type 5)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles a modal submission interaction (type 5).
    /// Parses the count and origin from the modal custom_id and advances to the action select step.
    /// </summary>
    /// <param name="interaction">The interaction event.</param>
    public async Task HandleModalSubmitAsync(InteractionCreateEvent interaction)
    {
        if (interaction.Data is null) return;

        try
        {
            var customId = interaction.Data.CustomId;
            if (customId is null || !customId.StartsWith("move_modal:", StringComparison.Ordinal))
            {
                _logger.Warning("Unexpected modal custom_id: {CustomId}", customId);
                return;
            }

            // Parse move_modal:{channelId}:{messageId}
            var parts = customId.Split(':');
            if (parts.Length < 3)
            {
                await RespondEphemeralAsync(interaction, "Invalid modal data.");
                return;
            }

            var channelId = parts[1];
            var messageId = parts[2];

            // Parse the count from the modal text input
            var countText = interaction.Data.Components?[0].Components?[0].Value ?? "1";
            if (!int.TryParse(countText, out var count) || count < 1)
            {
                await RespondEphemeralAsync(interaction, "Please enter a valid number (1 or more).");
                return;
            }

            var userId = GetUserId(interaction);
            var guildId = interaction.GuildId;
            if (userId is null || guildId is null)
            {
                await RespondEphemeralAsync(interaction, "Could not identify user or guild.");
                return;
            }

            // Create session
            var key = InteractionStateStore.Key(userId, guildId);
            var session = new InteractionSession
            {
                UserId = userId,
                GuildId = guildId,
                SourceChannelId = channelId,
                TargetMessageId = messageId,
                MessageCount = count
            };
            _stateStore.Set(key, session);

            // Respond with action select (Step 2)
            await RespondWithActionSelectAsync(interaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling modal submission");
            await TryRespondEphemeralAsync(interaction, $"Error: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 1: Context Menu handlers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleMoveMessageAsync(InteractionCreateEvent interaction)
    {
        var targetId = interaction.Data!.TargetId;
        if (targetId is null || interaction.ChannelId is null)
        {
            await RespondEphemeralAsync(interaction, "Could not find the target message.");
            return;
        }

        // Respond with modal asking for count
        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 9,
            data = new
            {
                custom_id = $"move_modal:{interaction.ChannelId}:{targetId}",
                title = "Move Messages",
                components = new object[]
                {
                    new
                    {
                        type = 1,
                        components = new object[]
                        {
                            new
                            {
                                type = 4,
                                custom_id = "count",
                                label = "How many messages to move?",
                                style = 1,
                                placeholder = "1",
                                required = true,
                                min_length = 1,
                                max_length = 4
                            }
                        }
                    }
                }
            }
        });
    }

    private async Task HandleMoveThisBelowAsync(InteractionCreateEvent interaction)
    {
        var targetId = interaction.Data!.TargetId;
        if (targetId is null || interaction.ChannelId is null)
        {
            await RespondEphemeralAsync(interaction, "Could not find the target message.");
            return;
        }

        var userId = GetUserId(interaction);
        var guildId = interaction.GuildId;
        if (userId is null || guildId is null)
        {
            await RespondEphemeralAsync(interaction, "Could not identify user or guild.");
            return;
        }

        // Skip modal — go straight to action select with count = -1 (all from here down)
        var key = InteractionStateStore.Key(userId, guildId);
        var session = new InteractionSession
        {
            UserId = userId,
            GuildId = guildId,
            SourceChannelId = interaction.ChannelId,
            TargetMessageId = targetId,
            MessageCount = -1
        };
        _stateStore.Set(key, session);

        // Respond with action select (Step 2)
        await RespondWithActionSelectAsync(interaction);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 2: Action select response helper
    // ─────────────────────────────────────────────────────────────────────────

    private Task RespondWithActionSelectAsync(InteractionCreateEvent interaction)
    {
        return _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 4,
            data = new
            {
                content = "Select an action to perform on the messages",
                flags = 64,
                components = new object[]
                {
                    new
                    {
                        type = 1,
                        components = new object[]
                        {
                            new
                            {
                                type = 3,
                                custom_id = "move_action",
                                options = new object[]
                                {
                                    new { label = "Repost into a channel", value = "channel", emoji = new { name = "\U0001F4E2" } },
                                    new { label = "Repost into a thread/forum", value = "thread", emoji = new { name = "\U0001F9F5" } }
                                }
                            }
                        }
                    }
                }
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 3: Action select -> Destination select
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleActionSelectAsync(InteractionCreateEvent interaction)
    {
        var session = GetSessionOrNull(interaction);
        if (session is null)
        {
            await RespondEphemeralAsync(interaction, "Session expired. Please start over.");
            return;
        }

        var selectedAction = interaction.Data!.Values?[0];
        if (selectedAction is null)
        {
            await RespondEphemeralAsync(interaction, "No action selected.");
            return;
        }

        session.Action = selectedAction;

        // Respond type 7 (UPDATE_MESSAGE) with channel select
        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 7,
            data = new
            {
                content = "Select the destination channel",
                components = new object[]
                {
                    new
                    {
                        type = 1,
                        components = new object[]
                        {
                            new
                            {
                                type = 8,
                                custom_id = "move_dest",
                                channel_types = new[] { 0, 5, 10, 11, 12, 15 }
                            }
                        }
                    }
                }
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 4: Destination select -> Confirmation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleDestinationSelectAsync(InteractionCreateEvent interaction)
    {
        var session = GetSessionOrNull(interaction);
        if (session is null)
        {
            await RespondEphemeralAsync(interaction, "Session expired. Please start over.");
            return;
        }

        var destId = interaction.Data!.Values?[0];
        if (destId is null)
        {
            await RespondEphemeralAsync(interaction, "No destination selected.");
            return;
        }

        session.DestinationId = destId;

        var countDisplay = session.MessageCount == -1 ? "all" : session.MessageCount.ToString();

        // Respond type 7 with confirmation
        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 7,
            data = new
            {
                content = $"**Ready to move {countDisplay} message(s)?**\nFrom: <#{session.SourceChannelId}>\nTo: <#{destId}>",
                components = new object[]
                {
                    new
                    {
                        type = 1,
                        components = new object[]
                        {
                            new { type = 2, style = 3, label = "Yes", custom_id = "move_yes", emoji = new { name = "\u2705" } },
                            new { type = 2, style = 4, label = "No", custom_id = "move_no", emoji = new { name = "\u274C" } }
                        }
                    }
                }
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 5a: Confirm Yes -> Execute Move
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleConfirmYesAsync(InteractionCreateEvent interaction)
    {
        var session = GetSessionOrNull(interaction);
        if (session is null)
        {
            await RespondEphemeralAsync(interaction, "Session expired. Please start over.");
            return;
        }

        if (session.DestinationId is null)
        {
            await RespondEphemeralAsync(interaction, "No destination set. Please start over.");
            return;
        }

        // Defer with type 6 (DEFERRED_UPDATE_MESSAGE)
        await _interactionEndpoints.DeferComponentAsync(interaction.Id, interaction.Token);

        // Fetch messages
        List<Message> messages;
        if (session.MessageCount == -1)
        {
            // All from target message downward
            messages = await FetchMessagesFromAsync(session.SourceChannelId, session.TargetMessageId!);
        }
        else if (session.TargetMessageId is not null)
        {
            // Specific count starting from target message
            messages = await FetchMessagesFromAsync(session.SourceChannelId, session.TargetMessageId);
            if (messages.Count > session.MessageCount)
                messages = messages.Take(session.MessageCount).ToList();
        }
        else
        {
            messages = [];
        }

        if (messages.Count == 0)
        {
            await EditOriginalAsync(interaction.Token, "No messages found to move.");
            return;
        }

        // Move messages
        var destId = session.DestinationId;
        int movedCount;

        if (session.Action == "thread")
        {
            // Create a thread in the destination
            var newThread = await _channelEndpoints.CreateThreadAsync(destId, "Moved Messages");
            movedCount = await MoveMessagesToDestinationAsync(messages, newThread.Id);
        }
        else
        {
            movedCount = await MoveMessagesToDestinationAsync(messages, destId);
        }

        // Store moved messages for potential deletion
        session.MovedMessages = messages;

        // Edit original with result + cleanup buttons
        await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
        {
            content = $"\u2705 Moved {movedCount} message(s) to <#{destId}>",
            components = new object[]
            {
                new
                {
                    type = 1,
                    components = new object[]
                    {
                        new { type = 2, style = 4, label = "Yes, delete original messages", custom_id = "move_delete" },
                        new { type = 2, style = 2, label = "No, keep original messages", custom_id = "move_keep" }
                    }
                }
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 5b: Confirm No -> Cancel
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleConfirmNoAsync(InteractionCreateEvent interaction)
    {
        var session = GetSessionOrNull(interaction);
        if (session is not null)
        {
            var userId = GetUserId(interaction);
            var guildId = interaction.GuildId;
            if (userId is not null && guildId is not null)
                _stateStore.Remove(InteractionStateStore.Key(userId, guildId));
        }

        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 7,
            data = new
            {
                content = "Move cancelled.",
                components = Array.Empty<object>()
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 6a: Delete originals
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleDeleteOriginalsAsync(InteractionCreateEvent interaction)
    {
        var session = GetSessionOrNull(interaction);
        if (session?.MovedMessages is null || session.MovedMessages.Count == 0)
        {
            await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
            {
                type = 7,
                data = new
                {
                    content = "No messages to delete (session may have expired).",
                    components = Array.Empty<object>()
                }
            });
            return;
        }

        // Defer type 6
        await _interactionEndpoints.DeferComponentAsync(interaction.Id, interaction.Token);

        var count = session.MovedMessages.Count;
        await DeleteMessagesAsync(session.SourceChannelId, session.MovedMessages);

        // Clean up session
        var userId = GetUserId(interaction);
        var guildId = interaction.GuildId;
        if (userId is not null && guildId is not null)
            _stateStore.Remove(InteractionStateStore.Key(userId, guildId));

        await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
        {
            content = $"\u2705 Moved and deleted {count} message(s).",
            components = Array.Empty<object>()
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 6b: Keep originals
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleKeepOriginalsAsync(InteractionCreateEvent interaction)
    {
        var userId = GetUserId(interaction);
        var guildId = interaction.GuildId;
        if (userId is not null && guildId is not null)
            _stateStore.Remove(InteractionStateStore.Key(userId, guildId));

        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 7,
            data = new
            {
                content = "\u2705 Done! Original messages kept.",
                components = Array.Empty<object>()
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Slash commands (simpler flow)
    // ─────────────────────────────────────────────────────────────────────────

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

        var messages = await FetchMessageRangeAsync(interaction.ChannelId, startId, endId);
        var movedCount = await MoveMessagesToDestinationAsync(messages, destId);

        // Store session for delete/keep buttons
        var userId = GetUserId(interaction);
        var guildId = interaction.GuildId;
        var sessionKey = userId is not null && guildId is not null
            ? InteractionStateStore.Key(userId, guildId)
            : null;

        if (sessionKey is not null)
        {
            _stateStore.Set(sessionKey, new InteractionSession
            {
                UserId = userId!,
                GuildId = guildId!,
                SourceChannelId = interaction.ChannelId,
                MessageCount = movedCount,
                MovedMessages = messages
            });
        }

        var deleteId = sessionKey is not null ? $"move_slash_delete:{sessionKey}" : "move_slash_delete:none";
        var keepId = sessionKey is not null ? $"move_slash_keep:{sessionKey}" : "move_slash_keep:none";

        await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
        {
            content = $"\u2705 Moved {movedCount} message(s) to <#{destId}>.",
            components = new object[]
            {
                new
                {
                    type = 1,
                    components = new object[]
                    {
                        new { type = 2, style = 4, label = "Yes, delete original messages", custom_id = deleteId },
                        new { type = 2, style = 2, label = "No, keep original messages", custom_id = keepId }
                    }
                }
            }
        });
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

        // Store session for delete/keep buttons
        var userId = GetUserId(interaction);
        var guildId = interaction.GuildId;
        var sessionKey = userId is not null && guildId is not null
            ? InteractionStateStore.Key(userId, guildId)
            : null;

        if (sessionKey is not null)
        {
            _stateStore.Set(sessionKey, new InteractionSession
            {
                UserId = userId!,
                GuildId = guildId!,
                SourceChannelId = threadId,
                MessageCount = movedCount,
                MovedMessages = messages
            });
        }

        var deleteId = sessionKey is not null ? $"move_slash_delete:{sessionKey}" : "move_slash_delete:none";
        var keepId = sessionKey is not null ? $"move_slash_keep:{sessionKey}" : "move_slash_keep:none";

        await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
        {
            content = $"\u2705 Moved {movedCount} message(s) from thread **{threadName}** to <#{newThread.Id}>.",
            components = new object[]
            {
                new
                {
                    type = 1,
                    components = new object[]
                    {
                        new { type = 2, style = 4, label = "Yes, delete original messages", custom_id = deleteId },
                        new { type = 2, style = 2, label = "No, keep original messages", custom_id = keepId }
                    }
                }
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Slash command delete/keep handlers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleSlashDeleteAsync(InteractionCreateEvent interaction, string customId)
    {
        var sessionKey = customId["move_slash_delete:".Length..];
        var session = _stateStore.Get(sessionKey);

        if (session?.MovedMessages is null || session.MovedMessages.Count == 0)
        {
            await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
            {
                type = 7,
                data = new
                {
                    content = "No messages to delete (session may have expired).",
                    components = Array.Empty<object>()
                }
            });
            return;
        }

        await _interactionEndpoints.DeferComponentAsync(interaction.Id, interaction.Token);

        var count = session.MovedMessages.Count;
        await DeleteMessagesAsync(session.SourceChannelId, session.MovedMessages);
        _stateStore.Remove(sessionKey);

        await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
        {
            content = $"\u2705 Moved and deleted {count} message(s).",
            components = Array.Empty<object>()
        });
    }

    private async Task HandleSlashKeepAsync(InteractionCreateEvent interaction, string customId)
    {
        var sessionKey = customId["move_slash_keep:".Length..];
        _stateStore.Remove(sessionKey);

        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 7,
            data = new
            {
                content = "\u2705 Done! Original messages kept.",
                components = Array.Empty<object>()
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Message moving logic
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<int> MoveMessagesToDestinationAsync(List<Message> messages, string destChannelId)
    {
        if (messages.Count == 0) return 0;

        // Create a webhook in the destination
        var webhook = await _webhookEndpoints.CreateWebhookAsync(destChannelId, "Conduit Move");

        if (string.IsNullOrEmpty(webhook.Token))
        {
            throw new InvalidOperationException("Failed to create webhook \u2014 missing token. Check bot permissions.");
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

    // ─────────────────────────────────────────────────────────────────────────
    //  Message fetching helpers
    // ─────────────────────────────────────────────────────────────────────────

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

        // Fetch messages around the target to include it
        var targetBatch = await _messageEndpoints.GetMessagesAsync(channelId, 100);
        var targetMsg = targetBatch.FirstOrDefault(m => m.Id == fromMessageId);
        if (targetMsg is not null) all.Add(targetMsg);

        // Then fetch everything after
        var afterId = fromMessageId;
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

    private async Task<List<Message>> FetchMessageRangeAsync(string sourceChannelId, string startId, string endId)
    {
        var messages = await FetchAllMessagesChronologicalAsync(sourceChannelId);
        return messages
            .Where(m => CompareSnowflakes(m.Id, startId) >= 0 && CompareSnowflakes(m.Id, endId) <= 0)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Utility helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string? GetUserId(InteractionCreateEvent interaction)
    {
        return interaction.Member?.User?.Id ?? interaction.User?.Id;
    }

    private InteractionSession? GetSessionOrNull(InteractionCreateEvent interaction)
    {
        var userId = GetUserId(interaction);
        var guildId = interaction.GuildId;
        if (userId is null || guildId is null) return null;
        return _stateStore.Get(InteractionStateStore.Key(userId, guildId));
    }

    private Task RespondEphemeralAsync(InteractionCreateEvent interaction, string message)
    {
        return _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 4,
            data = new { content = message, flags = 64 }
        });
    }

    private async Task TryRespondEphemeralAsync(InteractionCreateEvent interaction, string message)
    {
        try { await RespondEphemeralAsync(interaction, message); }
        catch (Exception ex) { _logger.Warning(ex, "Failed to send ephemeral response"); }
    }

    private async Task EditOriginalAsync(string interactionToken, string content)
    {
        await _interactionEndpoints.EditOriginalAsync(_applicationId, interactionToken, new
        {
            content,
            components = Array.Empty<object>()
        });
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
        if (ulong.TryParse(a, out var ua) && ulong.TryParse(b, out var ub))
            return ua.CompareTo(ub);
        return string.Compare(a, b, StringComparison.Ordinal);
    }
}
