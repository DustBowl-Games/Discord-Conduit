using System.Net;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Gateway;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Commands;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Tests.Fixtures;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DustBowlGames.DiscordConduit.Core.Tests;

/// <summary>Simple Serilog sink that collects log events in a list for assertions.</summary>
internal sealed class CollectingSink : ILogEventSink
{
    public List<LogEvent> Events { get; } = [];

    public void Emit(LogEvent logEvent) => Events.Add(logEvent);
}

public class MoveCommandHandlerTests : IDisposable
{
    private const string ApplicationId = "app-123";
    private const string UserId = "user-456";
    private const string GuildId = "guild-789";
    private const string ChannelId = "chan-100";
    private const string TargetMessageId = "msg-200";

    private readonly FakeHttpHandler _handler;
    private readonly DiscordRestClient _restClient;
    private readonly InteractionStateStore _stateStore;
    private readonly CollectingSink _logSink;
    private readonly ILogger _logger;
    private readonly MoveCommandHandler _sut;

    public MoveCommandHandlerTests()
    {
        _handler = new FakeHttpHandler();

        // Default: all interaction responses succeed (POST to /interactions/)
        _handler.Respond(HttpMethod.Post, "/interactions/", HttpStatusCode.NoContent);
        // Default: edit original succeeds (PATCH to /webhooks/)
        _handler.Respond(HttpMethod.Patch, "/webhooks/", HttpStatusCode.OK,
            new { id = "1", content = "ok" });

        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri(DiscordRestClient.BaseUrl) };
        _restClient = new DiscordRestClient("fake-token", Log.Logger, httpClient: httpClient);

        var messageEndpoints = new MessageEndpoints(_restClient);
        var webhookEndpoints = new WebhookEndpoints(_restClient);
        var channelEndpoints = new ChannelEndpoints(_restClient);
        var interactionEndpoints = new InteractionEndpoints(_restClient);

        _logSink = new CollectingSink();
        _logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(_logSink)
            .CreateLogger();

        var migrator = new MessageMigrator(_logger);
        var attachmentHandler = new AttachmentHandler(new HttpClient(_handler), _logger);

        _stateStore = new InteractionStateStore();

        _sut = new MoveCommandHandler(
            messageEndpoints,
            webhookEndpoints,
            channelEndpoints,
            interactionEndpoints,
            migrator,
            attachmentHandler,
            _stateStore,
            ApplicationId,
            _logger);
    }

    public void Dispose()
    {
        _stateStore.Dispose();
        _restClient.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static InteractionCreateEvent CreateCommandInteraction(
        string commandName, int commandType = 3, string? targetId = null,
        List<InteractionOption>? options = null)
    {
        return new InteractionCreateEvent
        {
            Id = "int-001",
            Type = 2, // ApplicationCommand
            Token = "int-token-abc",
            ChannelId = ChannelId,
            GuildId = GuildId,
            Member = new GuildMember
            {
                User = new User { Id = UserId, Username = "testuser" }
            },
            Data = new InteractionData
            {
                Name = commandName,
                Type = commandType,
                TargetId = targetId,
                Options = options
            }
        };
    }

    private static InteractionCreateEvent CreateComponentInteraction(string customId,
        List<string>? values = null)
    {
        return new InteractionCreateEvent
        {
            Id = "int-002",
            Type = 3, // MessageComponent
            Token = "int-token-def",
            ChannelId = ChannelId,
            GuildId = GuildId,
            Member = new GuildMember
            {
                User = new User { Id = UserId, Username = "testuser" }
            },
            Data = new InteractionData
            {
                CustomId = customId,
                Values = values
            }
        };
    }

    private string SessionKey => InteractionStateStore.Key(UserId, GuildId);

    // ─────────────────────────────────────────────────────────────────────
    //  1. GetCommandDefinitions
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetCommandDefinitions_Returns4Commands()
    {
        var commands = MoveCommandHandler.GetCommandDefinitions();
        Assert.Equal(4, commands.Count);
    }

    [Fact]
    public void GetCommandDefinitions_HasTwoContextMenuCommands()
    {
        var commands = MoveCommandHandler.GetCommandDefinitions();
        var contextMenus = commands.Where(c => c.Type == 3).ToList();
        Assert.Equal(2, contextMenus.Count);
        Assert.Contains(contextMenus, c => c.Name == "Move This");
        Assert.Contains(contextMenus, c => c.Name == "Move This & Below");
    }

    [Fact]
    public void GetCommandDefinitions_HasTwoSlashCommands()
    {
        var commands = MoveCommandHandler.GetCommandDefinitions();
        var slashCommands = commands.Where(c => c.Type == 1).ToList();
        Assert.Equal(2, slashCommands.Count);
        Assert.Contains(slashCommands, c => c.Name == "move-range");
        Assert.Contains(slashCommands, c => c.Name == "move-thread");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  2. HandleInteractionAsync — Move This (count=1)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleInteractionAsync_MoveThis_CreatesSessionWithCount1()
    {
        var interaction = CreateCommandInteraction(
            MoveCommandHandler.MoveThisCommand, commandType: 3, targetId: TargetMessageId);

        await _sut.HandleInteractionAsync(interaction);

        var session = _stateStore.Get(SessionKey);
        Assert.NotNull(session);
        Assert.Equal(1, session.MessageCount);
        Assert.Equal(TargetMessageId, session.TargetMessageId);
        Assert.Equal(ChannelId, session.SourceChannelId);
        Assert.Equal(UserId, session.UserId);
        Assert.Equal(GuildId, session.GuildId);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  3. HandleInteractionAsync — Move This & Below (count=-1)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleInteractionAsync_MoveThisBelow_CreatesSessionWithCountMinus1()
    {
        var interaction = CreateCommandInteraction(
            MoveCommandHandler.MoveThisBelowCommand, commandType: 3, targetId: TargetMessageId);

        await _sut.HandleInteractionAsync(interaction);

        var session = _stateStore.Get(SessionKey);
        Assert.NotNull(session);
        Assert.Equal(-1, session.MessageCount);
        Assert.Equal(TargetMessageId, session.TargetMessageId);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  4. HandleComponentAsync — action select stores action
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleComponentAsync_ActionSelect_StoresActionInSession()
    {
        // Pre-create a session (as if Move This was already invoked)
        _stateStore.Set(SessionKey, new InteractionSession
        {
            SessionId = "sess-1",
            UserId = UserId,
            GuildId = GuildId,
            SourceChannelId = ChannelId,
            TargetMessageId = TargetMessageId,
            MessageCount = 1
        });

        var interaction = CreateComponentInteraction("move_action:sess-1", values: ["channel"]);

        await _sut.HandleComponentAsync(interaction);

        var session = _stateStore.Get(SessionKey);
        Assert.NotNull(session);
        Assert.Equal("channel", session.Action);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  5. HandleComponentAsync — confirm no cleans up session
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleComponentAsync_ConfirmNo_CleansUpSession()
    {
        // Pre-create a session
        _stateStore.Set(SessionKey, new InteractionSession
        {
            SessionId = "sess-1",
            UserId = UserId,
            GuildId = GuildId,
            SourceChannelId = ChannelId,
            TargetMessageId = TargetMessageId,
            MessageCount = 1,
            Action = "channel",
            DestinationId = "dest-300"
        });

        var interaction = CreateComponentInteraction("move_no:sess-1");

        await _sut.HandleComponentAsync(interaction);

        var session = _stateStore.Get(SessionKey);
        Assert.Null(session);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  6. HandleComponentAsync — unknown custom_id is logged
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleComponentAsync_UnknownCustomId_IsLogged()
    {
        var interaction = CreateComponentInteraction("totally_unknown");

        await _sut.HandleComponentAsync(interaction);

        // No session should be created or modified
        var session = _stateStore.Get(SessionKey);
        Assert.Null(session);

        // Verify warning was logged via collecting sink
        Assert.Contains(_logSink.Events, e =>
            e.Level == LogEventLevel.Warning &&
            e.MessageTemplate.Text.Contains("Unknown component custom_id"));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  7. Session lifecycle — create, update through steps, cleanup
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SessionLifecycle_CreateUpdateAndCleanup()
    {
        // Step 1: Invoke "Move This" command — creates session
        var cmdInteraction = CreateCommandInteraction(
            MoveCommandHandler.MoveThisCommand, commandType: 3, targetId: TargetMessageId);
        await _sut.HandleInteractionAsync(cmdInteraction);

        var session = _stateStore.Get(SessionKey);
        Assert.NotNull(session);
        Assert.Equal(1, session.MessageCount);
        Assert.Null(session.Action);
        Assert.Null(session.DestinationId);

        // Step 2: Select action — updates session.Action. The session's per-flow
        // nonce is generated during command handling, so read it back and embed it
        // in the component custom_ids the way the real UI does.
        var sessionId = session.SessionId;
        var actionInteraction = CreateComponentInteraction($"move_action:{sessionId}", values: ["as_thread"]);
        await _sut.HandleComponentAsync(actionInteraction);

        session = _stateStore.Get(SessionKey);
        Assert.NotNull(session);
        Assert.Equal("as_thread", session.Action);

        // Step 3: Cancel — cleans up session
        var cancelInteraction = CreateComponentInteraction($"move_no:{sessionId}");
        await _sut.HandleComponentAsync(cancelInteraction);

        session = _stateStore.Get(SessionKey);
        Assert.Null(session);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Edge cases
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleInteractionAsync_NullData_DoesNotThrow()
    {
        var interaction = new InteractionCreateEvent
        {
            Id = "int-003",
            Type = 2,
            Token = "tok",
            Data = null
        };

        await _sut.HandleInteractionAsync(interaction);

        // Should be a no-op — no session created
        var session = _stateStore.Get(SessionKey);
        Assert.Null(session);
    }

    [Fact]
    public async Task HandleComponentAsync_NullData_DoesNotThrow()
    {
        var interaction = new InteractionCreateEvent
        {
            Id = "int-004",
            Type = 3,
            Token = "tok",
            Data = null
        };

        await _sut.HandleComponentAsync(interaction);
    }

    [Fact]
    public async Task HandleComponentAsync_ActionSelect_NoSession_RespondsSessionExpired()
    {
        // No session pre-created
        var interaction = CreateComponentInteraction("move_action", values: ["channel"]);

        await _sut.HandleComponentAsync(interaction);

        // Verify the interaction response was sent (POST to /interactions/)
        var postRequests = _handler.SentRequests
            .Where(r => r.Method == HttpMethod.Post &&
                        (r.RequestUri?.ToString().Contains("/interactions/") ?? false))
            .ToList();
        Assert.NotEmpty(postRequests);
    }

    [Fact]
    public async Task HandleInteractionAsync_MoveThis_NoTargetId_RespondsEphemeral()
    {
        // Create interaction with no target_id
        var interaction = CreateCommandInteraction(
            MoveCommandHandler.MoveThisCommand, commandType: 3, targetId: null);

        await _sut.HandleInteractionAsync(interaction);

        // No session should be created
        var session = _stateStore.Get(SessionKey);
        Assert.Null(session);
    }

    [Fact]
    public async Task HandleComponentAsync_KeepOriginals_CleansUpSession()
    {
        _stateStore.Set(SessionKey, new InteractionSession
        {
            SessionId = "sess-1",
            UserId = UserId,
            GuildId = GuildId,
            SourceChannelId = ChannelId,
            TargetMessageId = TargetMessageId,
            MessageCount = 1,
            Action = "channel",
            DestinationId = "dest-300",
            MovedMessageIds = ["m1"]
        });

        var interaction = CreateComponentInteraction("move_keep:sess-1");

        await _sut.HandleComponentAsync(interaction);

        Assert.Null(_stateStore.Get(SessionKey));
    }
}
