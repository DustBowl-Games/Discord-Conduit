using System.Text.Json;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Gateway;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Commands;

/// <summary>
/// Handles all move-related slash commands and context menu commands using a
/// multi-step interaction flow with modals, select menus, and buttons.
/// Commands: Move This, Move This &amp; Below, move-range, move-thread.
/// Actions: Repost into channel, repost into thread/forum, repost as thread, repost as forum.
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

    /// <summary>Command name for the "Move This" context menu command.</summary>
    public const string MoveThisCommand = "Move This";

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
            new ApplicationCommand { Name = MoveThisCommand, Type = 3, Description = string.Empty, DefaultMemberPermissions = "8192" },
            new ApplicationCommand { Name = MoveThisBelowCommand, Type = 3, Description = string.Empty, DefaultMemberPermissions = "8192" },
            // Slash commands (type 1 = CHAT_INPUT)
            new ApplicationCommand
            {
                Name = MoveRangeCommand,
                Type = 1,
                Description = "Move a range of messages between two message IDs",
                DefaultMemberPermissions = "8192",
                Options =
                [
                    new ApplicationCommandOption { Name = "start", Type = 3, Description = "ID of the first message", Required = true },
                    new ApplicationCommandOption { Name = "end", Type = 3, Description = "ID of the last message", Required = true }
                ]
            },
            new ApplicationCommand
            {
                Name = MoveThreadCommand,
                Type = 1,
                Description = "Move a thread or forum post to another channel",
                DefaultMemberPermissions = "8192",
                Options =
                [
                    new ApplicationCommandOption { Name = "thread", Type = 7, Description = "Thread or forum post to move", Required = true }
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
                case MoveThisCommand:
                    await HandleMoveThisAsync(interaction).ConfigureAwait(false);
                    break;
                case MoveThisBelowCommand:
                    await HandleMoveThisBelowAsync(interaction).ConfigureAwait(false);
                    break;
                case MoveRangeCommand:
                    await HandleMoveRangeAsync(interaction).ConfigureAwait(false);
                    break;
                case MoveThreadCommand:
                    await HandleMoveThreadAsync(interaction).ConfigureAwait(false);
                    break;
                default:
                    _logger.Warning("Unknown command: {CommandName}", interaction.Data.Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling command {CommandName}", interaction.Data.Name);
            // The exception may have been thrown before any ACK was sent, in which case
            // EditOriginal would fail because no original response exists. Attempt an
            // ephemeral response first, then fall back to editing the original.
            await TryRespondEphemeralAsync(interaction, SanitizeErrorMessage(ex)).ConfigureAwait(false);
            await TryEditOriginalAsync(interaction.Token, SanitizeErrorMessage(ex)).ConfigureAwait(false);
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
        if (interaction.Data is null)
        {
            await TryRespondEphemeralAsync(interaction, "This action is no longer available. Please start over.").ConfigureAwait(false);
            return;
        }

        var customId = interaction.Data.CustomId;
        if (customId is null)
        {
            await TryRespondEphemeralAsync(interaction, "This action is no longer available. Please start over.").ConfigureAwait(false);
            return;
        }

        // The handler is chosen by the base portion of the custom_id (before the ':').
        // The suffix is a per-flow session nonce validated inside each handler.
        var baseId = ParseBaseCustomId(customId);

        try
        {
            switch (baseId)
            {
                case "move_action":
                    await HandleActionSelectAsync(interaction, customId).ConfigureAwait(false);
                    break;
                case "move_dest":
                    await HandleDestinationSelectAsync(interaction, customId).ConfigureAwait(false);
                    break;
                case "move_yes":
                    await HandleConfirmYesAsync(interaction, customId).ConfigureAwait(false);
                    break;
                case "move_no":
                    await HandleConfirmNoAsync(interaction, customId).ConfigureAwait(false);
                    break;
                case "move_delete":
                    await HandleDeleteOriginalsAsync(interaction, customId).ConfigureAwait(false);
                    break;
                case "move_keep":
                    await HandleKeepOriginalsAsync(interaction, customId).ConfigureAwait(false);
                    break;
                default:
                    _logger.Warning("Unknown component custom_id: {CustomId}", customId);
                    await TryRespondEphemeralAsync(interaction,
                        "This action is no longer available. Please start over.").ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling component interaction {CustomId}", customId);
            // An exception may have been thrown before any ACK was sent, in which case
            // EditOriginal would fail. Try an ephemeral response first, then fall back.
            await TryRespondEphemeralAsync(interaction, SanitizeErrorMessage(ex)).ConfigureAwait(false);
            await TryEditOriginalAsync(interaction.Token, SanitizeErrorMessage(ex)).ConfigureAwait(false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Entry point 3: Modal submissions (type 5) — currently unused but reserved
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles a modal submission interaction (type 5). Currently not used in the flow.
    /// </summary>
    /// <param name="interaction">The interaction event.</param>
    public Task HandleModalSubmitAsync(InteractionCreateEvent interaction)
    {
        _logger.Debug("Received modal submit (unused): {CustomId}", interaction.Data?.CustomId);
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 1: Command handlers — create session, show action select
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleMoveThisAsync(InteractionCreateEvent interaction)
    {
        var targetId = interaction.Data!.TargetId;
        if (targetId is null || interaction.ChannelId is null)
        {
            await RespondEphemeralAsync(interaction, "Could not find the target message.").ConfigureAwait(false);
            return;
        }

        var session = CreateSession(interaction, interaction.ChannelId, targetId, messageCount: 1);
        if (session is null) { await RespondEphemeralAsync(interaction, "This command only works inside a server, not in DMs. Try again from a server channel.").ConfigureAwait(false); return; }

        await RespondWithActionSelectAsync(interaction, session).ConfigureAwait(false);
    }

    private async Task HandleMoveThisBelowAsync(InteractionCreateEvent interaction)
    {
        var targetId = interaction.Data!.TargetId;
        if (targetId is null || interaction.ChannelId is null)
        {
            await RespondEphemeralAsync(interaction, "Could not find the target message.").ConfigureAwait(false);
            return;
        }

        // -1 = all from target downward
        var session = CreateSession(interaction, interaction.ChannelId, targetId, messageCount: -1);
        if (session is null) { await RespondEphemeralAsync(interaction, "This command only works inside a server, not in DMs. Try again from a server channel.").ConfigureAwait(false); return; }

        await RespondWithActionSelectAsync(interaction, session).ConfigureAwait(false);
    }

    private async Task HandleMoveRangeAsync(InteractionCreateEvent interaction)
    {
        var options = interaction.Data!.Options;
        if (options is null || interaction.ChannelId is null)
        {
            await RespondEphemeralAsync(interaction, "Missing required options.").ConfigureAwait(false);
            return;
        }

        var startId = GetOptionString(options, "start");
        var endId = GetOptionString(options, "end");

        if (startId is null || endId is null)
        {
            await RespondEphemeralAsync(interaction, "Please provide both start and end message IDs.").ConfigureAwait(false);
            return;
        }

        // Reject non-snowflake IDs before they flow into REST URL paths. A value like
        // "x/../../guilds/999/bans" would otherwise be URL-normalized into a different
        // authenticated API route (path-injection / confused-deputy).
        if (!Snowflake.IsValid(startId) || !Snowflake.IsValid(endId))
        {
            await RespondEphemeralAsync(interaction, "Message IDs must be numeric Discord IDs.").ConfigureAwait(false);
            return;
        }

        // -2 = range mode (use TargetMessageId as start, EndMessageId as end)
        var session = CreateSession(interaction, interaction.ChannelId, startId, messageCount: -2);
        if (session is null) { await RespondEphemeralAsync(interaction, "This command only works inside a server, not in DMs. Try again from a server channel.").ConfigureAwait(false); return; }
        session.EndMessageId = endId;

        await RespondWithActionSelectAsync(interaction, session).ConfigureAwait(false);
    }

    private async Task HandleMoveThreadAsync(InteractionCreateEvent interaction)
    {
        var options = interaction.Data!.Options;
        if (options is null)
        {
            await RespondEphemeralAsync(interaction, "Missing required options.").ConfigureAwait(false);
            return;
        }

        var threadId = GetOptionString(options, "thread");
        if (threadId is null)
        {
            await RespondEphemeralAsync(interaction, "Please provide a thread or forum post.").ConfigureAwait(false);
            return;
        }

        // Reject non-snowflake IDs before they flow into REST URL paths (path-injection guard).
        // Done BEFORE the permission check so a malformed ID can never reach channel resolution.
        if (!Snowflake.IsValid(threadId))
        {
            await RespondEphemeralAsync(interaction, "Thread must be a valid Discord channel.").ConfigureAwait(false);
            return;
        }

        // Authorization (confused-deputy guard): the bot would read ALL messages from this
        // user-supplied channel using the BOT's permissions. Require that the INVOKING USER
        // can actually view and read history in that channel, using Discord's own computed
        // permissions from the resolved channel object. If they are unavailable or the
        // required bits are missing, refuse before creating a session or fetching anything.
        if (!UserCanReadResolvedChannel(interaction, threadId))
        {
            await RespondEphemeralAsync(interaction, "You don't have permission to read that channel.").ConfigureAwait(false);
            return;
        }

        // -3 = thread mode (move all messages from this thread)
        var session = CreateSession(interaction, threadId, targetMessageId: null, messageCount: -3);
        if (session is null) { await RespondEphemeralAsync(interaction, "This command only works inside a server, not in DMs. Try again from a server channel.").ConfigureAwait(false); return; }

        await RespondWithActionSelectAsync(interaction, session).ConfigureAwait(false);
    }

    // VIEW_CHANNEL (0x400) and READ_MESSAGE_HISTORY (0x10000) permission bits.
    private const ulong PermViewChannel = 0x400;
    private const ulong PermReadMessageHistory = 0x10000;

    /// <summary>
    /// Returns true if the invoking user's Discord-computed permissions for the resolved
    /// channel include both VIEW_CHANNEL and READ_MESSAGE_HISTORY. Returns false if the
    /// resolved permissions are unavailable, unparsable, or missing either bit.
    /// </summary>
    private static bool UserCanReadResolvedChannel(InteractionCreateEvent interaction, string channelId)
    {
        var channels = interaction.Data?.Resolved?.Channels;
        if (channels is null || !channels.TryGetValue(channelId, out var resolved))
            return false;

        if (resolved.Permissions is null || !ulong.TryParse(resolved.Permissions, out var perms))
            return false;

        const ulong required = PermViewChannel | PermReadMessageHistory;
        return (perms & required) == required;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 2: Action select
    // ─────────────────────────────────────────────────────────────────────────

    private Task RespondWithActionSelectAsync(InteractionCreateEvent interaction, InteractionSession session)
    {
        return _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 4,
            data = new
            {
                content = "**What would you like to do?**",
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
                                custom_id = ComponentId("move_action", session.SessionId),
                                placeholder = "Select an action...",
                                options = new object[]
                                {
                                    new { label = "Repost into a channel", value = "channel", description = "Move messages to an existing channel", emoji = new { name = "\U0001F4E2" } },
                                    new { label = "Repost into a thread/forum", value = "thread", description = "Move messages to an existing thread or forum post", emoji = new { name = "\U0001F9F5" } },
                                    new { label = "Repost as a new thread", value = "as_thread", description = "Create a new thread and move messages into it", emoji = new { name = "\U0001F195" } },
                                    new { label = "Repost as a new forum post", value = "as_forum", description = "Create a new forum post and move messages into it", emoji = new { name = "\U0001F4CB" } }
                                }
                            }
                        }
                    }
                }
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 3: Action select → Destination select
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleActionSelectAsync(InteractionCreateEvent interaction, string customId)
    {
        var session = await GetValidatedSessionAsync(interaction, customId).ConfigureAwait(false);
        if (session is null) return;

        var selectedAction = interaction.Data!.Values?[0];
        if (selectedAction is null)
        {
            await RespondEphemeralAsync(interaction, "No action selected.").ConfigureAwait(false);
            return;
        }

        session.Action = selectedAction;

        if (selectedAction == "thread")
        {
            // Threads don't appear in the channel select component — fetch active threads
            // and present them in a string select menu. The fetch is a rate-limited HTTP
            // GET, so we MUST ACK within Discord's 3s deadline BEFORE making it. Defer the
            // component update (type 6) first, then render the select via EditOriginal.
            await ShowThreadSelectAsync(interaction, session).ConfigureAwait(false);
        }
        else
        {
            // Use the native channel select for channels and forum channels.
            // This path performs no network I/O before responding, so a synchronous
            // type-7 (UPDATE_MESSAGE) response is safe and stays within the ACK deadline.
            var prompt = selectedAction switch
            {
                "channel" => "**Select the destination channel**",
                "as_thread" => "**Select the channel to create a new thread in**",
                "as_forum" => "**Select the forum channel to create a new post in**",
                _ => "**Select the destination**"
            };

            var channelTypes = selectedAction switch
            {
                "channel" => new[] { 0, 5 },
                "as_thread" => new[] { 0, 5 },
                "as_forum" => new[] { 15 },
                _ => new[] { 0, 5, 15 }
            };

            await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
            {
                type = 7,
                data = new
                {
                    content = prompt,
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
                                    custom_id = ComponentId("move_dest", session.SessionId),
                                    placeholder = "Search for a channel...",
                                    channel_types = channelTypes
                                }
                            }
                        }
                    }
                }
            }).ConfigureAwait(false);
        }
    }

    private async Task ShowThreadSelectAsync(InteractionCreateEvent interaction, InteractionSession session)
    {
        // ACK first (type 6, DEFERRED_UPDATE_MESSAGE) so the network fetch below does not
        // blow past Discord's 3-second response deadline. All subsequent UI for this path
        // is rendered by editing the original (now-deferred) response.
        await _interactionEndpoints.DeferComponentAsync(interaction.Id, interaction.Token).ConfigureAwait(false);

        List<Channel> threads;
        try
        {
            threads = await _channelEndpoints.GetActiveThreadsAsync(session.GuildId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch active threads");
            await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
            {
                content = "Failed to load threads. Please try again.",
                components = Array.Empty<object>()
            }).ConfigureAwait(false);
            return;
        }

        if (threads.Count == 0)
        {
            await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
            {
                content = "No active threads found in this server.",
                components = Array.Empty<object>()
            }).ConfigureAwait(false);
            return;
        }

        // Discord string select menus support max 25 options
        var options = threads
            .Take(25)
            .Select(t => new
            {
                label = TruncateLabel(t.Name ?? "Unnamed Thread"),
                value = t.Id,
                description = t.Type switch
                {
                    10 => "Announcement Thread",
                    11 => "Public Thread",
                    12 => "Private Thread",
                    15 => "Forum Post",
                    _ => "Thread"
                }
            })
            .ToArray();

        await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
        {
            content = "**Select the destination thread or forum post**",
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
                            custom_id = ComponentId("move_dest", session.SessionId),
                            placeholder = "Select a thread...",
                            options
                        }
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    private static string TruncateLabel(string label)
    {
        return label.Length > 100 ? label[..97] + "..." : label;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 4: Destination select → Confirmation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleDestinationSelectAsync(InteractionCreateEvent interaction, string customId)
    {
        var session = await GetValidatedSessionAsync(interaction, customId).ConfigureAwait(false);
        if (session is null) return;

        var destId = interaction.Data!.Values?[0];
        if (destId is null)
        {
            await RespondEphemeralAsync(interaction, "No destination selected.").ConfigureAwait(false);
            return;
        }

        session.DestinationId = destId;

        var countDisplay = session.MessageCount switch
        {
            -1 => "all (from target to end)",
            -2 => "range",
            -3 => "all (entire thread)",
            _ => session.MessageCount.ToString()
        };

        // Keep these phrases identical to the action-select option labels so the wording for the
        // chosen action doesn't change between the select step and this confirmation step.
        var actionDisplay = session.Action switch
        {
            "channel" => "Repost into a channel",
            "thread" => "Repost into a thread/forum",
            "as_thread" => "Repost as a new thread",
            "as_forum" => "Repost as a new forum post",
            _ => session.Action ?? "unknown"
        };

        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 7,
            data = new
            {
                content = $"**Ready to move?**\n" +
                          $"\u2022 Messages: **{countDisplay}**\n" +
                          $"\u2022 From: <#{session.SourceChannelId}>\n" +
                          $"\u2022 To: <#{destId}>\n" +
                          $"\u2022 Action: **{actionDisplay}**",
                components = new object[]
                {
                    new
                    {
                        type = 1,
                        components = new object[]
                        {
                            new { type = 2, style = 3, label = "Yes, move them!", custom_id = ComponentId("move_yes", session.SessionId), emoji = new { name = "\u2705" } },
                            new { type = 2, style = 4, label = "Cancel", custom_id = ComponentId("move_no", session.SessionId), emoji = new { name = "\u274C" } }
                        }
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 5a: Confirm Yes → Execute Move
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleConfirmYesAsync(InteractionCreateEvent interaction, string customId)
    {
        var session = await GetValidatedSessionAsync(interaction, customId).ConfigureAwait(false);
        if (session is null) return;

        if (session.DestinationId is null || session.Action is null)
        {
            await RespondEphemeralAsync(interaction, "Missing destination or action. Please start over.").ConfigureAwait(false);
            return;
        }

        // Defer with type 6 (DEFERRED_UPDATE_MESSAGE)
        await _interactionEndpoints.DeferComponentAsync(interaction.Id, interaction.Token).ConfigureAwait(false);

        // Fetch messages based on the mode
        List<Message> messages;
        switch (session.MessageCount)
        {
            case -3:
                // Thread mode: all messages from the thread
                messages = await FetchAllMessagesChronologicalAsync(session.SourceChannelId).ConfigureAwait(false);
                break;
            case -2:
                // Range mode: messages between start and end IDs
                messages = await FetchMessageRangeAsync(session.SourceChannelId,
                    session.TargetMessageId!, session.EndMessageId!).ConfigureAwait(false);
                break;
            case -1:
                // "This & Below": from target to end of channel
                messages = await FetchMessagesFromAsync(session.SourceChannelId, session.TargetMessageId!).ConfigureAwait(false);
                break;
            default:
                // "Move This" (count = 1) or explicit count
                if (session.TargetMessageId is not null)
                {
                    messages = await FetchMessagesFromAsync(session.SourceChannelId, session.TargetMessageId).ConfigureAwait(false);
                    if (messages.Count > session.MessageCount)
                        messages = messages.Take(session.MessageCount).ToList();
                }
                else
                {
                    messages = [];
                }
                break;
        }

        if (messages.Count == 0)
        {
            await EditOriginalAsync(interaction.Token, "No messages found to move.").ConfigureAwait(false);
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            await ExecuteMoveAsync(interaction, session, messages, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Move timed out after 10 minutes");
            await TryEditOriginalAsync(interaction.Token, "Move timed out after 10 minutes.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Move execution failed");
            await TryEditOriginalAsync(interaction.Token, SanitizeErrorMessage(ex)).ConfigureAwait(false);
        }
    }

    private async Task ExecuteMoveAsync(InteractionCreateEvent interaction, InteractionSession session, List<Message> messages, CancellationToken ct)
    {
        // Determine the webhook channel and optional thread ID based on the action
        var destChannelId = session.DestinationId!;
        string webhookChannelId;  // where to create the webhook
        string? threadId = null;  // if posting into a thread, pass this to webhook execution
        string displayDestId;     // for the user-facing message

        switch (session.Action)
        {
            case "as_thread":
            {
                // Create a new thread in the selected text channel; the webhook goes in the channel.
                var newThread = await _channelEndpoints.CreateThreadAsync(destChannelId, "Moved Messages", ct).ConfigureAwait(false);
                webhookChannelId = destChannelId;
                threadId = newThread.Id;
                displayDestId = newThread.Id;
                break;
            }
            case "as_forum":
            {
                // Create a new forum post in the selected forum channel. Forum threads require an
                // initial message, so create the post with a starter; migrated messages follow via webhook.
                // If the source is itself a forum post, carry its tags across (mapped by name).
                var appliedTags = await MapForumTagsAsync(session.SourceChannelId, destChannelId, ct).ConfigureAwait(false);
                var newPost = await _channelEndpoints.CreateForumPostAsync(
                    destChannelId, "Moved Messages", "\U0001F4E6 Migrated messages", appliedTags, ct).ConfigureAwait(false);
                webhookChannelId = destChannelId;
                threadId = newPost.Id;
                displayDestId = newPost.Id;
                break;
            }
            case "thread":
            {
                // Posting into an existing thread — webhook must be in the parent channel
                try
                {
                    var threadChannel = await _channelEndpoints.GetChannelAsync(destChannelId).ConfigureAwait(false);
                    webhookChannelId = threadChannel.ParentId ?? destChannelId;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.Warning(ex, "Destination channel {ChannelId} not found", destChannelId);
                    await TryEditOriginalAsync(interaction.Token,
                        "\u274C Could not find the destination channel. It may have been deleted.").ConfigureAwait(false);
                    return;
                }

                threadId = destChannelId;
                displayDestId = destChannelId;
                break;
            }
            default:
                // "channel" — post directly, webhook in the destination channel
                webhookChannelId = destChannelId;
                displayDestId = destChannelId;
                break;
        }

        List<string> movedIds;
        try
        {
            movedIds = await MoveMessagesToDestinationAsync(messages, webhookChannelId, threadId, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _logger.Warning(ex, "Missing Manage Webhooks permission in channel {ChannelId}", webhookChannelId);
            await TryEditOriginalAsync(interaction.Token,
                "\u274C Bot lacks **Manage Webhooks** permission in the destination channel. Please grant this permission and try again.").ConfigureAwait(false);
            return;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.Warning(ex, "Destination channel {ChannelId} not found during move", webhookChannelId);
            await TryEditOriginalAsync(interaction.Token,
                "\u274C Could not find the destination channel. It may have been deleted.").ConfigureAwait(false);
            return;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Move failed for channel {ChannelId}", webhookChannelId);
            await TryEditOriginalAsync(interaction.Token,
                $"\u274C {SanitizeErrorMessage(ex)}").ConfigureAwait(false);
            return;
        }

        var movedCount = movedIds.Count;

        // Leave a breadcrumb in the source channel
        try
        {
            var userId = GetUserId(interaction);
            await _messageEndpoints.CreateMessageAsync(
                session.SourceChannelId,
                $"\U0001F4E6 **{movedCount} message(s) moved to <#{displayDestId}>** by <@{userId}>",
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to post move breadcrumb in source channel");
        }

        // Store moved message IDs for potential deletion of the originals. Only the IDs are
        // retained on the session \u2014 the source channel is already on the session, and the
        // 14-day bulk-delete cutoff is recomputed from each snowflake ID at delete time.
        session.MovedMessageIds = movedIds;

        // Edit original with result + cleanup buttons
        await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
        {
            content = $"\u2705 **Moved {movedCount} message(s) to <#{displayDestId}>**\n\nWant me to clean up the originals?",
            components = new object[]
            {
                new
                {
                    type = 1,
                    components = new object[]
                    {
                        new { type = 2, style = 4, label = "Yes, delete originals", custom_id = ComponentId("move_delete", session.SessionId), emoji = new { name = "\U0001F5D1" } },
                        new { type = 2, style = 2, label = "No, keep originals", custom_id = ComponentId("move_keep", session.SessionId) }
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 5b: Confirm No → Cancel
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleConfirmNoAsync(InteractionCreateEvent interaction, string customId)
    {
        // Only the owning session may cancel; ignore stale/superseded clicks.
        var session = await GetValidatedSessionAsync(interaction, customId).ConfigureAwait(false);
        if (session is null) return;

        CleanupSession(interaction);

        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 7,
            data = new
            {
                content = "\u274C Move cancelled.",
                components = Array.Empty<object>()
            }
        }).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 6a: Delete originals
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleDeleteOriginalsAsync(InteractionCreateEvent interaction, string customId)
    {
        var session = await GetValidatedSessionAsync(interaction, customId).ConfigureAwait(false);
        if (session is null) return;

        // Defense-in-depth for a destructive step: explicitly require the clicking user to be
        // the user who owns this session. The keying already enforces this, but the deletion
        // path must fail closed if it is ever reached with a mismatched user.
        if (GetUserId(interaction) != session.UserId)
        {
            await TryRespondEphemeralAsync(interaction, "You can't perform this action.").ConfigureAwait(false);
            return;
        }

        if (session.MovedMessageIds is null || session.MovedMessageIds.Count == 0)
        {
            await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
            {
                type = 7,
                data = new
                {
                    content = "No messages to delete (session may have expired).",
                    components = Array.Empty<object>()
                }
            }).ConfigureAwait(false);
            return;
        }

        await _interactionEndpoints.DeferComponentAsync(interaction.Id, interaction.Token).ConfigureAwait(false);

        var count = session.MovedMessageIds.Count;
        await DeleteMessagesAsync(session.SourceChannelId, session.MovedMessageIds).ConfigureAwait(false);
        CleanupSession(interaction);

        await _interactionEndpoints.EditOriginalAsync(_applicationId, interaction.Token, new
        {
            content = $"\u2705 Moved and deleted {count} original message(s).",
            components = Array.Empty<object>()
        }).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Step 6b: Keep originals
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleKeepOriginalsAsync(InteractionCreateEvent interaction, string customId)
    {
        // Only the owning session may finalize; ignore stale/superseded clicks.
        var session = await GetValidatedSessionAsync(interaction, customId).ConfigureAwait(false);
        if (session is null) return;

        CleanupSession(interaction);

        await _interactionEndpoints.RespondAsync(interaction.Id, interaction.Token, new
        {
            type = 7,
            data = new
            {
                content = "\u2705 Done! Original messages kept.",
                components = Array.Empty<object>()
            }
        }).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Message moving logic
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When moving into a forum, maps the source forum post's tags to the destination forum's tags
    /// by name, returning the destination tag IDs to apply (max 5). Returns an empty list when the
    /// source isn't a tagged forum post or no tag names line up. Best-effort; never throws.
    /// </summary>
    private async Task<List<string>> MapForumTagsAsync(string sourceChannelId, string destForumChannelId, CancellationToken ct)
    {
        try
        {
            var source = await _channelEndpoints.GetChannelAsync(sourceChannelId, ct).ConfigureAwait(false);
            if (source.AppliedTags is not { Count: > 0 } || source.ParentId is null)
                return [];

            // Resolve the source applied-tag IDs to names via the source's parent forum.
            var sourceForum = await _channelEndpoints.GetChannelAsync(source.ParentId, ct).ConfigureAwait(false);
            var names = (sourceForum.AvailableTags ?? [])
                .Where(t => t.Name is not null && source.AppliedTags.Contains(t.Id))
                .Select(t => t.Name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0)
                return [];

            // Map the names to the destination forum's tag IDs.
            var dest = await _channelEndpoints.GetChannelAsync(destForumChannelId, ct).ConfigureAwait(false);
            return (dest.AvailableTags ?? [])
                .Where(t => t.Name is not null && names.Contains(t.Name))
                .Select(t => t.Id)
                .Take(5)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to map forum tags from {Source} to {Dest}", sourceChannelId, destForumChannelId);
            return [];
        }
    }

    /// <summary>
    /// Reposts the given messages via a temporary webhook and returns the source IDs that were
    /// actually reposted (system, empty, and failed messages are excluded), so the caller can
    /// safely offer to delete only the originals that were genuinely migrated.
    /// </summary>
    private async Task<List<string>> MoveMessagesToDestinationAsync(List<Message> messages, string webhookChannelId, string? threadId = null, CancellationToken ct = default)
    {
        if (messages.Count == 0) return new List<string>();

        var webhook = await _webhookEndpoints.CreateWebhookAsync(webhookChannelId, "Conduit Move", ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(webhook.Token))
        {
            throw new InvalidOperationException("Failed to create webhook \u2014 missing token. Check bot permissions.");
        }

        var movedIds = new List<string>();

        try
        {
            foreach (var message in messages)
            {
                ct.ThrowIfCancellationRequested();

                if (!message.IsRegularMessage) continue;

                var content = _messageMigrator.BuildWebhookContent(message, replyReference: null, includeStickers: true);
                var username = _messageMigrator.GetWebhookUsername(message);
                var avatarUrl = _messageMigrator.GetWebhookAvatarUrl(message);

                _logger.Debug("Moving message {Id}: user={User}, avatar={Avatar}, content={Content}",
                    message.Id, username, avatarUrl, content?[..Math.Min(content?.Length ?? 0, 50)]);

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

                // Defensively sanitize forwarded bot embeds (cap count, strip non-http URLs).
                richEmbeds = EmbedSanitizer.Sanitize(richEmbeds);

                // Re-create an attached poll if present (votes/original timing can't be carried over).
                var poll = message.Poll?.ToCreateRequest();

                // Skip messages that have no content, no attachments, no embeds, and no poll
                var hasContent = !string.IsNullOrEmpty(content);
                var hasFiles = uploadable.Count > 0;
                var hasEmbeds = richEmbeds is { Count: > 0 };

                if (!hasContent && !hasFiles && !hasEmbeds && poll is null)
                {
                    _logger.Debug("Skipping empty message {MessageId}", message.Id);
                    continue;
                }

                try
                {
                    if (hasFiles)
                    {
                        var files = new List<(byte[] Data, string Filename, string? ContentType)>();
                        foreach (var attachment in uploadable)
                        {
                            var downloaded = await _attachmentHandler.DownloadAttachmentAsync(attachment, ct).ConfigureAwait(false);
                            files.Add(downloaded);
                        }

                        await _webhookEndpoints.ExecuteWebhookWithFilesAsync(
                            webhook.Id, webhook.Token!,
                            () => _attachmentHandler.CreateMultipartContent(content, username, avatarUrl, richEmbeds, files, poll),
                            ct, threadId).ConfigureAwait(false);
                    }
                    else
                    {
                        var payload = new WebhookExecutePayload
                        {
                            Content = content,
                            Username = username,
                            AvatarUrl = avatarUrl,
                            Embeds = richEmbeds,
                            Poll = poll
                        };

                        await _webhookEndpoints.ExecuteWebhookAsync(
                            webhook.Id, webhook.Token!, payload, ct, threadId).ConfigureAwait(false);
                    }

                    movedIds.Add(message.Id);
                }
                catch (OperationCanceledException)
                {
                    throw; // Let cancellation propagate
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to move message {MessageId}", message.Id);
                }
            }
        }
        finally
        {
            try { await _webhookEndpoints.DeleteWebhookAsync(webhook.Id, ct).ConfigureAwait(false); }
            catch { /* best effort */ }
        }

        return movedIds;
    }

    private async Task DeleteMessagesAsync(string channelId, List<string> messageIds, CancellationToken ct = default)
    {
        var fourteenDaysAgo = DateTimeOffset.UtcNow.AddDays(-14);
        var recentIds = new List<string>();
        var oldIds = new List<string>();

        foreach (var id in messageIds)
        {
            // Derive the message creation time from the snowflake ID. Discord's bulk-delete
            // endpoint only accepts messages younger than 14 days; older ones must be deleted
            // individually.
            var createdAt = SnowflakeToTimestamp(id);
            if (createdAt is { } ts && ts > fourteenDaysAgo)
                recentIds.Add(id);
            else
                oldIds.Add(id);
        }

        // Bulk delete recent messages in batches of 100
        for (var i = 0; i < recentIds.Count; i += 100)
        {
            ct.ThrowIfCancellationRequested();
            var batch = recentIds.Skip(i).Take(100).ToList();
            if (batch.Count >= 2)
            {
                try { await _messageEndpoints.BulkDeleteMessagesAsync(channelId, batch, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.Warning(ex, "Bulk delete failed, falling back to individual delete"); }
            }
            else if (batch.Count == 1)
            {
                try { await _messageEndpoints.DeleteMessageAsync(channelId, batch[0], ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.Warning(ex, "Failed to delete message {Id}", batch[0]); }
            }
        }

        // Delete old messages individually
        foreach (var id in oldIds)
        {
            ct.ThrowIfCancellationRequested();
            try { await _messageEndpoints.DeleteMessageAsync(channelId, id, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
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
            var batch = await _messageEndpoints.GetMessagesAsync(channelId, 100, before: beforeId).ConfigureAwait(false);
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
        var all = new List<Message>();

        // Fetch the target message directly by ID
        try
        {
            var targetMsg = await _messageEndpoints.GetMessageAsync(channelId, fromMessageId).ConfigureAwait(false);
            all.Add(targetMsg);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to fetch target message {Id}", fromMessageId);
            return all;
        }

        // Fetch all messages after the target (chronologically)
        var afterId = fromMessageId;
        while (true)
        {
            var batch = await _messageEndpoints.GetMessagesAsync(channelId, 100, after: afterId).ConfigureAwait(false);
            if (batch.Count == 0) break;
            // Discord returns newest-first even with after=, so reverse to chronological order
            batch.Reverse();
            all.AddRange(batch);
            afterId = batch[^1].Id;
            if (batch.Count < 100) break;
        }

        return all;
    }

    private async Task<List<Message>> FetchMessageRangeAsync(string sourceChannelId, string startId, string endId)
    {
        // Ensure startId < endId (chronological order)
        if (CompareSnowflakes(startId, endId) > 0)
            (startId, endId) = (endId, startId);

        var all = new List<Message>();

        // Fetch the start message
        try
        {
            var startMsg = await _messageEndpoints.GetMessageAsync(sourceChannelId, startId).ConfigureAwait(false);
            all.Add(startMsg);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to fetch start message {Id}", startId);
        }

        // Fetch messages after start until we pass end
        var afterId = startId;
        while (true)
        {
            var batch = await _messageEndpoints.GetMessagesAsync(sourceChannelId, 100, after: afterId).ConfigureAwait(false);
            if (batch.Count == 0) break;
            batch.Reverse();

            foreach (var msg in batch)
            {
                if (CompareSnowflakes(msg.Id, endId) > 0) break;
                all.Add(msg);
            }

            // If the last message in the batch is past the end, we're done
            if (batch.Any(m => CompareSnowflakes(m.Id, endId) >= 0)) break;
            afterId = batch[^1].Id;
            if (batch.Count < 100) break;
        }

        return all;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Utility helpers
    // ─────────────────────────────────────────────────────────────────────────

    private InteractionSession? CreateSession(InteractionCreateEvent interaction, string sourceChannelId,
        string? targetMessageId, int messageCount)
    {
        var userId = GetUserId(interaction);
        var guildId = interaction.GuildId;
        if (userId is null || guildId is null) return null;

        var key = InteractionStateStore.Key(userId, guildId);
        var session = new InteractionSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            GuildId = guildId,
            SourceChannelId = sourceChannelId,
            TargetMessageId = targetMessageId,
            MessageCount = messageCount
        };
        _stateStore.Set(key, session);
        return session;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Component custom_id helpers
    //
    //  Every component the flow emits carries a per-flow session nonce in its
    //  custom_id, formatted as "{base}:{sessionId}". The base part selects the
    //  handler; the sessionId part is validated against the looked-up session so
    //  a click on a stale/superseded ephemeral cannot act on a newer session.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Builds a component custom_id of the form "{baseId}:{sessionId}".</summary>
    private static string ComponentId(string baseId, string sessionId) => $"{baseId}:{sessionId}";

    /// <summary>Returns the portion of a custom_id before the first ':' (the handler selector).</summary>
    private static string ParseBaseCustomId(string customId)
    {
        var idx = customId.IndexOf(':');
        return idx < 0 ? customId : customId[..idx];
    }

    /// <summary>Returns the portion of a custom_id after the first ':' (the session nonce), or null.</summary>
    private static string? ParseSessionId(string customId)
    {
        var idx = customId.IndexOf(':');
        return idx < 0 || idx == customId.Length - 1 ? null : customId[(idx + 1)..];
    }

    /// <summary>
    /// Resolves the session for a component interaction and validates that the custom_id's
    /// embedded session nonce matches. Returns null and replies ephemerally on mismatch/expiry.
    /// </summary>
    private async Task<InteractionSession?> GetValidatedSessionAsync(InteractionCreateEvent interaction, string customId)
    {
        var session = GetSessionOrNull(interaction);
        var sessionId = ParseSessionId(customId);

        if (session is null || sessionId is null || session.SessionId != sessionId)
        {
            await TryRespondEphemeralAsync(interaction,
                "This action is from an expired or superseded step. Please start over.").ConfigureAwait(false);
            return null;
        }

        return session;
    }

    private void CleanupSession(InteractionCreateEvent interaction)
    {
        var userId = GetUserId(interaction);
        var guildId = interaction.GuildId;
        if (userId is not null && guildId is not null)
            _stateStore.Remove(InteractionStateStore.Key(userId, guildId));
    }

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
            // Defense-in-depth: suppress all mentions so any future content interpolation
            // (error text, IDs) can never ping a user, role, or @everyone/@here.
            data = new { content = message, flags = 64, allowed_mentions = new { parse = Array.Empty<string>() } }
        });
    }

    private async Task TryRespondEphemeralAsync(InteractionCreateEvent interaction, string message)
    {
        try { await RespondEphemeralAsync(interaction, message).ConfigureAwait(false); }
        catch (Exception ex) { _logger.Warning(ex, "Failed to send ephemeral response"); }
    }

    private async Task EditOriginalAsync(string interactionToken, string content)
    {
        await _interactionEndpoints.EditOriginalAsync(_applicationId, interactionToken, new
        {
            content,
            components = Array.Empty<object>()
        }).ConfigureAwait(false);
    }

    private async Task TryEditOriginalAsync(string interactionToken, string content)
    {
        try { await EditOriginalAsync(interactionToken, content).ConfigureAwait(false); }
        catch (Exception ex) { _logger.Warning(ex, "Failed to edit interaction response"); }
    }

    private static string SanitizeErrorMessage(Exception ex)
    {
        // Don't expose internal details like URLs (which may contain webhook tokens)
        if (ex is HttpRequestException httpEx)
        {
            return httpEx.StatusCode switch
            {
                System.Net.HttpStatusCode.Forbidden => "Permission denied. Check bot permissions.",
                System.Net.HttpStatusCode.NotFound => "Resource not found. The channel or message may have been deleted.",
                System.Net.HttpStatusCode.TooManyRequests => "Rate limited by Discord. Please try again in a moment.",
                _ => $"Discord API error ({(int?)httpEx.StatusCode}). Please try again."
            };
        }
        return "An unexpected error occurred. Check bot logs for details.";
    }

    private static string? GetOptionString(List<InteractionOption> options, string name)
    {
        var option = options.FirstOrDefault(o => o.Name == name);
        if (option?.Value is JsonElement element)
            return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        return option?.Value?.ToString();
    }

    private static int CompareSnowflakes(string a, string b)
    {
        // All IDs reaching this point are validated upstream (slash-command options are checked
        // with Snowflake.IsValid before a session is created, and message IDs come from Discord).
        // A non-numeric value here is therefore a programming error, not user input — fail loudly
        // rather than silently falling back to an ordinal string compare that could mis-order IDs.
        if (!ulong.TryParse(a, out var ua) || !ulong.TryParse(b, out var ub))
            throw new ArgumentException($"CompareSnowflakes received a non-numeric snowflake: '{a}' / '{b}'.");
        return ua.CompareTo(ub);
    }

    // Discord epoch (2015-01-01T00:00:00Z) in Unix milliseconds.
    private const long DiscordEpochMs = 1420070400000L;

    /// <summary>
    /// Converts a Discord snowflake ID to its creation timestamp, or null if it cannot be parsed.
    /// The high 42 bits of a snowflake encode milliseconds since the Discord epoch.
    /// </summary>
    private static DateTimeOffset? SnowflakeToTimestamp(string id)
    {
        if (!ulong.TryParse(id, out var snowflake))
            return null;

        var unixMs = (long)(snowflake >> 22) + DiscordEpochMs;
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
    }
}
