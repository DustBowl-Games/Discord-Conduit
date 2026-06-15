using System.Net;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Tests.Fixtures;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

/// <summary>
/// End-to-end tests for <see cref="MigrationEngine"/> driven through <see cref="FakeHttpHandler"/>,
/// which serves multiple registrations for the same route in sequence (so per-message webhook
/// responses are distinct) and matches query-string URLs by substring.
/// </summary>
public class MigrationEngineTests : IDisposable
{
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();
    private readonly string _tempDir;

    public MigrationEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MigrationEngineTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    // --- Helpers ---

    private static object MakeMessageJson(
        string id,
        int type = 0,
        string content = "hello",
        string authorId = "100",
        string authorUsername = "testuser",
        string? authorGlobalName = null,
        string? authorAvatar = null,
        object[]? attachments = null,
        object[]? stickerItems = null,
        object[]? reactions = null)
    {
        return new
        {
            id,
            channel_id = "src-chan",
            author = new
            {
                id = authorId,
                username = authorUsername,
                global_name = authorGlobalName,
                avatar = authorAvatar
            },
            content,
            timestamp = "2024-01-01T00:00:00Z",
            type,
            attachments = attachments ?? Array.Empty<object>(),
            embeds = Array.Empty<object>(),
            sticker_items = stickerItems,
            reactions
        };
    }

    private static object MakeWebhookJson(string id = "wh-1", string token = "wh-token")
    {
        return new { id, token, channel_id = "dst-chan", type = 1, name = "Conduit Migration" };
    }

    /// <summary>
    /// Creates a reposted message JSON object returned by webhook execute.
    /// </summary>
    private static object MakeRepostedMessageJson(string id)
    {
        return new
        {
            id,
            channel_id = "dst-chan",
            author = new { id = "wh-bot", username = "Conduit Migration" },
            content = "migrated",
            timestamp = "2024-01-01T00:00:01Z",
            type = 0,
            attachments = Array.Empty<object>(),
            embeds = Array.Empty<object>()
        };
    }

    private static MigrationOptions DefaultOptions(bool dryRun = false, bool includeReactions = false)
    {
        return new MigrationOptions("src-chan", "dst-chan", "guild-1", DryRun: dryRun, IncludeReactions: includeReactions);
    }

    private MigrationEngine CreateEngine(FakeHttpHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(DiscordRestClient.BaseUrl) };
        var restClient = new DiscordRestClient("fake-bot-token", _logger, httpClient: httpClient);

        var messageEndpoints = new MessageEndpoints(restClient);
        var webhookEndpoints = new WebhookEndpoints(restClient);
        var reactionEndpoints = new ReactionEndpoints(restClient);
        var channelEndpoints = new ChannelEndpoints(restClient);
        var messageMigrator = new MessageMigrator(_logger);
        var attachmentHandler = new AttachmentHandler(new HttpClient(handler), _logger);

        return new MigrationEngine(
            messageEndpoints,
            webhookEndpoints,
            reactionEndpoints,
            channelEndpoints,
            messageMigrator,
            attachmentHandler,
            _tempDir,
            _logger);
    }

    private static Progress<MigrationProgress> NoOpProgress() => new(_ => { });

    // --- PreviewAsync ---

    [Fact]
    public async Task PreviewAsync_ReturnsCorrectCounts()
    {
        var messages = new[]
        {
            MakeMessageJson("1", content: "msg 1",
                attachments: new object[]
                {
                    new { id = "a1", filename = "small.png", size = 1024L, url = "https://cdn.example.com/small.png" }
                }),
            MakeMessageJson("2", content: "msg 2",
                attachments: new object[]
                {
                    new { id = "a2", filename = "big.zip", size = Attachment.MaxBotUploadSize + 1, url = "https://cdn.example.com/big.zip" }
                }),
            MakeMessageJson("3", content: "msg 3",
                stickerItems: new object[]
                {
                    new { id = "s1", name = "sticker" }
                }),
            MakeMessageJson("4", content: "msg 4")
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/src-chan/messages", messages);

        var engine = CreateEngine(handler);
        var preview = await engine.PreviewAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(4, preview.MessageCount);
        Assert.Equal(2, preview.AttachmentCount);
        Assert.Single(preview.OversizedAttachments);
        Assert.Equal("big.zip", preview.OversizedAttachments[0].Filename);
        // Warnings: one for oversized, one for stickers
        Assert.Equal(2, preview.Warnings.Count);
    }

    [Fact]
    public async Task PreviewAsync_CountsAllMessageTypes_IncludingSystem()
    {
        // PreviewAsync counts ALL messages (including system) — it does not filter.
        // The total message count includes system messages; skipping happens at migration time.
        var messages = new[]
        {
            MakeMessageJson("1", type: 0, content: "regular"),
            MakeMessageJson("2", type: 7, content: ""),      // guild member join (system)
            MakeMessageJson("3", type: 19, content: "reply"), // reply (regular)
            MakeMessageJson("4", type: 8, content: "")        // premium guild subscription (system)
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/src-chan/messages", messages);

        var engine = CreateEngine(handler);
        var preview = await engine.PreviewAsync(DefaultOptions(), CancellationToken.None);

        // PreviewAsync counts all messages, system ones included
        Assert.Equal(4, preview.MessageCount);
    }

    // --- RunAsync ---

    [Fact]
    public async Task RunAsync_MigratesMessagesViaWebhookInChronologicalOrder()
    {
        // FetchAllMessagesDescendingAsync returns messages newest-first (like Discord),
        // then the engine reverses them for chronological order.
        // With < 100 messages, only one batch is fetched.
        var messages = new[]
        {
            MakeMessageJson("3", content: "third"),
            MakeMessageJson("2", content: "second"),
            MakeMessageJson("1", content: "first"),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/src-chan/messages", messages)
            .RespondJson(HttpMethod.Post, "/channels/dst-chan/webhooks", MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/wh-1", HttpStatusCode.NoContent);

        // Register three webhook execute responses (one per message, in chronological order)
        handler
            .RespondJson(HttpMethod.Post, "/webhooks/wh-1/wh-token", MakeRepostedMessageJson("r1"))
            .RespondJson(HttpMethod.Post, "/webhooks/wh-1/wh-token", MakeRepostedMessageJson("r2"))
            .RespondJson(HttpMethod.Post, "/webhooks/wh-1/wh-token", MakeRepostedMessageJson("r3"));

        var engine = CreateEngine(handler);
        var progressReports = new List<MigrationProgress>();
        var progress = new Progress<MigrationProgress>(p => progressReports.Add(p));

        var result = await engine.RunAsync(DefaultOptions(), progress, CancellationToken.None);

        Assert.Equal(3, result.TotalMigrated);
        Assert.Equal(0, result.TotalFailed);
        Assert.Equal(0, result.TotalSkipped);

        // Verify webhook was created and deleted
        var webhookCreateReq = handler.SentRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Post && r.RequestUri!.ToString().Contains("/channels/dst-chan/webhooks"));
        Assert.NotNull(webhookCreateReq);

        var webhookDeleteReq = handler.SentRequests
            .FirstOrDefault(r => r.Method == HttpMethod.Delete && r.RequestUri!.ToString().Contains("/webhooks/wh-1"));
        Assert.NotNull(webhookDeleteReq);
    }

    [Fact]
    public async Task RunAsync_SkipsSystemMessages()
    {
        var messages = new[]
        {
            MakeMessageJson("3", type: 7, content: ""),       // system: guild member join
            MakeMessageJson("2", type: 0, content: "normal"),  // regular
            MakeMessageJson("1", type: 8, content: ""),        // system: premium sub
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/src-chan/messages", messages)
            .RespondJson(HttpMethod.Post, "/channels/dst-chan/webhooks", MakeWebhookJson())
            .RespondJson(HttpMethod.Post, "/webhooks/wh-1/wh-token", MakeRepostedMessageJson("r1"))
            .Respond(HttpMethod.Delete, "/webhooks/wh-1", HttpStatusCode.NoContent);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(DefaultOptions(), NoOpProgress(), CancellationToken.None);

        Assert.Equal(1, result.TotalMigrated);
        Assert.Equal(2, result.TotalSkipped);
        Assert.Equal(0, result.TotalFailed);
    }

    [Fact]
    public async Task RunAsync_HandlesEmptySourceChannel()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/src-chan/messages", Array.Empty<object>())
            .RespondJson(HttpMethod.Post, "/channels/dst-chan/webhooks", MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/wh-1", HttpStatusCode.NoContent);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(DefaultOptions(), NoOpProgress(), CancellationToken.None);

        Assert.Equal(0, result.TotalMigrated);
        Assert.Equal(0, result.TotalFailed);
        Assert.Equal(0, result.TotalSkipped);
    }

    [Fact]
    public async Task RunAsync_RespectsCancellationToken()
    {
        // Return enough messages so the engine enters the loop, then cancel after webhook creation.
        var messages = new[]
        {
            MakeMessageJson("2", content: "second"),
            MakeMessageJson("1", content: "first"),
        };

        var cts = new CancellationTokenSource();

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/src-chan/messages", messages)
            .RespondJson(HttpMethod.Post, "/channels/dst-chan/webhooks", MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/wh-1", HttpStatusCode.NoContent);

        // Don't register any webhook execute responses — cancel before first migration attempt.
        // The engine calls ct.ThrowIfCancellationRequested() at the start of the foreach loop.
        cts.Cancel();

        var engine = CreateEngine(handler);

        // TaskCanceledException extends OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunAsync(DefaultOptions(), NoOpProgress(), cts.Token));
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotCreateWebhookOrPostMessages()
    {
        var messages = new[]
        {
            MakeMessageJson("2", content: "second"),
            MakeMessageJson("1", content: "first"),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/src-chan/messages", messages);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(DefaultOptions(dryRun: true), NoOpProgress(), CancellationToken.None);

        Assert.Equal(2, result.TotalMigrated);
        Assert.Equal(0, result.TotalFailed);

        // No webhook creation or execution requests
        var webhookRequests = handler.SentRequests
            .Where(r => r.RequestUri!.ToString().Contains("/webhooks"))
            .ToList();
        Assert.Empty(webhookRequests);
    }

    // --- ResumeAsync ---

    [Fact]
    public async Task ResumeAsync_PicksUpFromLastMigratedMessage()
    {
        // Simulate that message "1" was already migrated; messages "2" and "3" remain.
        var remainingMessages = new[]
        {
            // Discord returns after= results in descending order; the engine reverses each batch.
            MakeMessageJson("3", content: "third"),
            MakeMessageJson("2", content: "second"),
        };

        var handler = new FakeHttpHandler()
            // after= fetch for remaining messages
            .RespondJson(HttpMethod.Get, "/channels/src-chan/messages", remainingMessages)
            // Webhook still exists
            .RespondJson(HttpMethod.Get, "/webhooks/wh-1", MakeWebhookJson())
            // Webhook execute for remaining messages
            .RespondJson(HttpMethod.Post, "/webhooks/wh-1/wh-token", MakeRepostedMessageJson("r2"))
            .RespondJson(HttpMethod.Post, "/webhooks/wh-1/wh-token", MakeRepostedMessageJson("r3"))
            .Respond(HttpMethod.Delete, "/webhooks/wh-1", HttpStatusCode.NoContent);

        var state = new MigrationState
        {
            MigrationId = "test-resume-migration",
            SourceChannelId = "src-chan",
            DestinationChannelId = "dst-chan",
            GuildId = "guild-1",
            WebhookId = "wh-1",
            WebhookToken = "wh-token",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Phase = MigrationPhase.MigratingMessages,
            MigratedCount = 1,
            LastSuccessfulSourceMessageId = "1",
            MessageIdMap = new Dictionary<string, string> { ["1"] = "r1" },
            Options = DefaultOptions()
        };

        var engine = CreateEngine(handler);
        var result = await engine.ResumeAsync(state, NoOpProgress(), CancellationToken.None);

        // Resume migrates the remaining messages (engine tracks total including previously migrated)
        Assert.True(result.TotalMigrated >= 2, $"Expected at least 2 migrated, got {result.TotalMigrated}");
        Assert.Equal(0, result.TotalFailed);
    }

    [Fact]
    public async Task ResumeAsync_NoLastMessageId_FetchesAllMessages()
    {
        // If LastSuccessfulSourceMessageId is null, the engine fetches all messages from scratch.
        var messages = new[]
        {
            MakeMessageJson("2", content: "second"),
            MakeMessageJson("1", content: "first"),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/src-chan/messages", messages)
            .RespondJson(HttpMethod.Get, "/webhooks/wh-1", MakeWebhookJson())
            .RespondJson(HttpMethod.Post, "/webhooks/wh-1/wh-token", MakeRepostedMessageJson("r1"))
            .RespondJson(HttpMethod.Post, "/webhooks/wh-1/wh-token", MakeRepostedMessageJson("r2"))
            .Respond(HttpMethod.Delete, "/webhooks/wh-1", HttpStatusCode.NoContent);

        var state = new MigrationState
        {
            MigrationId = "test-resume-from-start",
            SourceChannelId = "src-chan",
            DestinationChannelId = "dst-chan",
            GuildId = "guild-1",
            WebhookId = "wh-1",
            WebhookToken = "wh-token",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Phase = MigrationPhase.MigratingMessages,
            MigratedCount = 0,
            LastSuccessfulSourceMessageId = null,
            Options = DefaultOptions()
        };

        var engine = CreateEngine(handler);
        var result = await engine.ResumeAsync(state, NoOpProgress(), CancellationToken.None);

        Assert.Equal(2, result.TotalMigrated);
        Assert.Equal(0, result.TotalFailed);
    }
}
