using System.Net;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Tests.Fixtures;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

/// <summary>
/// Integration tests for the Tier-1 migration features — message filtering, pin re-pinning, and
/// poll re-creation — driven end-to-end through <see cref="MigrationEngine"/> and a
/// <see cref="FakeHttpHandler"/>. Realistic numeric snowflake IDs are used throughout so the
/// engine's snowflake validation is exercised.
/// </summary>
public class MigrationEngineTier1Tests : IDisposable
{
    private const string SourceChannel = "1000000000000000001";
    private const string DestChannel = "2000000000000000002";
    private const string Guild = "3000000000000000003";
    private const string WebhookId = "4000000000000000004";
    private const string WebhookToken = "wh-token";

    // URL fragments for matching/asserting against SentRequests.
    private const string MessagesUrl = "/channels/" + SourceChannel + "/messages";
    private const string WebhookCreateUrl = "/channels/" + DestChannel + "/webhooks";
    private const string WebhookExecuteUrl = "/webhooks/" + WebhookId + "/" + WebhookToken;
    private const string PinUrlPrefix = "/channels/" + DestChannel + "/pins/";

    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();
    private readonly string _tempDir;

    public MigrationEngineTier1Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MigrationEngineTier1Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    // --- Helpers ---

    /// <summary>
    /// Builds a source-message JSON object. Extends the base shape with optional <paramref name="pinned"/>
    /// and an optional <paramref name="poll"/> object so the Tier-1 paths can be exercised.
    /// </summary>
    private static object MakeMessageJson(
        string id,
        int type = 0,
        string? content = "hello",
        string authorId = "100",
        string authorUsername = "testuser",
        bool pinned = false,
        object? poll = null,
        object[]? attachments = null)
    {
        return new
        {
            id,
            channel_id = SourceChannel,
            author = new
            {
                id = authorId,
                username = authorUsername,
                global_name = (string?)null,
                avatar = (string?)null
            },
            content,
            timestamp = "2024-01-01T00:00:00Z",
            type,
            pinned,
            poll,
            attachments = attachments ?? Array.Empty<object>(),
            embeds = Array.Empty<object>(),
            sticker_items = (object[]?)null,
            reactions = (object[]?)null
        };
    }

    /// <summary>Builds a poll JSON object (question + answer texts) for embedding in a source message.</summary>
    private static object MakePollJson(string question, params string[] answers)
    {
        return new
        {
            question = new { text = question },
            answers = answers.Select(a => new { poll_media = new { text = a } }).ToArray(),
            allow_multiselect = false,
            layout_type = 1
        };
    }

    private static object MakeWebhookJson()
    {
        return new { id = WebhookId, token = WebhookToken, channel_id = DestChannel, type = 1, name = "Conduit Migration" };
    }

    private static object MakeRepostedMessageJson(string id)
    {
        return new
        {
            id,
            channel_id = DestChannel,
            author = new { id = "9000000000000000009", username = "Conduit Migration" },
            content = "migrated",
            timestamp = "2024-01-01T00:00:01Z",
            type = 0,
            attachments = Array.Empty<object>(),
            embeds = Array.Empty<object>()
        };
    }

    private static MigrationOptions Options(
        bool includePins = true,
        bool includePolls = true,
        MessageFilter? filter = null)
    {
        return new MigrationOptions(
            SourceChannel,
            DestChannel,
            Guild,
            DryRun: false,
            IncludeReactions: false,
            IncludePins: includePins,
            IncludePolls: includePolls,
            Filter: filter);
    }

    private MigrationEngine CreateEngine(FakeHttpHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(DiscordRestClient.BaseUrl) };
        var restClient = new DiscordRestClient("fake-bot-token", _logger, httpClient: httpClient);

        return new MigrationEngine(
            new MessageEndpoints(restClient),
            new WebhookEndpoints(restClient),
            new ReactionEndpoints(restClient),
            new ChannelEndpoints(restClient),
            new MessageMigrator(_logger),
            new AttachmentHandler(new HttpClient(handler), _logger),
            _tempDir,
            _logger);
    }

    private static Progress<MigrationProgress> NoOpProgress() => new(_ => { });

    /// <summary>Registers <paramref name="count"/> sequential webhook-execute responses (r1, r2, ...).</summary>
    private static void RegisterWebhookExecutes(FakeHttpHandler handler, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            handler.RespondJson(HttpMethod.Post, WebhookExecuteUrl, MakeRepostedMessageJson("500000000000000000" + i));
        }
    }

    private static int CountWebhookExecutePosts(FakeHttpHandler handler) =>
        handler.SentRequests.Count(r =>
            r.Method == HttpMethod.Post &&
            r.RequestUri!.ToString().Contains(WebhookExecuteUrl, StringComparison.Ordinal));

    /// <summary>Returns the captured request bodies of every webhook-execute POST.</summary>
    private static List<string> WebhookExecuteBodies(FakeHttpHandler handler) =>
        handler.SentRequestsWithBody
            .Where(r => r.Method == HttpMethod.Post && r.Url.Contains(WebhookExecuteUrl, StringComparison.Ordinal))
            .Select(r => r.Body)
            .ToList();

    // --- Filtering ---

    [Fact]
    public async Task RunAsync_FilterByAuthorId_OnlyMatchingMessagesAreReposted()
    {
        // Three messages from two authors; the filter keeps only author "100" (two messages).
        var messages = new[]
        {
            MakeMessageJson("10000000000000003", content: "third", authorId: "200"),
            MakeMessageJson("10000000000000002", content: "second", authorId: "100"),
            MakeMessageJson("10000000000000001", content: "first", authorId: "100"),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, MessagesUrl, messages)
            .RespondJson(HttpMethod.Post, WebhookCreateUrl, MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/" + WebhookId, HttpStatusCode.NoContent);
        RegisterWebhookExecutes(handler, 2);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(
            Options(filter: new MessageFilter(AuthorId: "100")), NoOpProgress(), CancellationToken.None);

        Assert.Equal(2, result.TotalMigrated);
        Assert.Equal(0, result.TotalFailed);
        Assert.Equal(2, CountWebhookExecutePosts(handler));
    }

    [Fact]
    public async Task RunAsync_FilterByContentContains_OnlyMatchingMessagesAreReposted()
    {
        var messages = new[]
        {
            MakeMessageJson("10000000000000003", content: "keep this one"),
            MakeMessageJson("10000000000000002", content: "drop me"),
            MakeMessageJson("10000000000000001", content: "also KEEP me"),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, MessagesUrl, messages)
            .RespondJson(HttpMethod.Post, WebhookCreateUrl, MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/" + WebhookId, HttpStatusCode.NoContent);
        RegisterWebhookExecutes(handler, 2);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(
            Options(filter: new MessageFilter(ContentContains: "keep")), NoOpProgress(), CancellationToken.None);

        // Case-insensitive: "keep this one" and "also KEEP me" match; "drop me" does not.
        Assert.Equal(2, result.TotalMigrated);
        Assert.Equal(2, CountWebhookExecutePosts(handler));
    }

    // --- Pins ---

    [Fact]
    public async Task RunAsync_PinnedSourceMessage_IssuesPinRequestForRepostedMessage()
    {
        var messages = new[]
        {
            MakeMessageJson("10000000000000002", content: "pinned one", pinned: true),
            MakeMessageJson("10000000000000001", content: "normal one", pinned: false),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, MessagesUrl, messages)
            .RespondJson(HttpMethod.Post, WebhookCreateUrl, MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/" + WebhookId, HttpStatusCode.NoContent)
            // Pin route: PUT /channels/{dest}/pins/{repostedId} -> 204 No Content.
            .Respond(HttpMethod.Put, PinUrlPrefix, HttpStatusCode.NoContent);
        RegisterWebhookExecutes(handler, 2);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(Options(includePins: true), NoOpProgress(), CancellationToken.None);

        Assert.Equal(2, result.TotalMigrated);

        // Exactly one PUT pin request — only the pinned source message is re-pinned.
        var pinRequests = handler.SentRequests
            .Where(r => r.Method == HttpMethod.Put &&
                        r.RequestUri!.ToString().Contains(PinUrlPrefix, StringComparison.Ordinal))
            .ToList();
        Assert.Single(pinRequests);
    }

    [Fact]
    public async Task RunAsync_NoPinnedMessages_IssuesNoPinRequests()
    {
        var messages = new[]
        {
            MakeMessageJson("10000000000000002", content: "two", pinned: false),
            MakeMessageJson("10000000000000001", content: "one", pinned: false),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, MessagesUrl, messages)
            .RespondJson(HttpMethod.Post, WebhookCreateUrl, MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/" + WebhookId, HttpStatusCode.NoContent)
            .Respond(HttpMethod.Put, PinUrlPrefix, HttpStatusCode.NoContent);
        RegisterWebhookExecutes(handler, 2);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(Options(includePins: true), NoOpProgress(), CancellationToken.None);

        Assert.Equal(2, result.TotalMigrated);

        var pinRequests = handler.SentRequests
            .Where(r => r.Method == HttpMethod.Put &&
                        r.RequestUri!.ToString().Contains(PinUrlPrefix, StringComparison.Ordinal))
            .ToList();
        Assert.Empty(pinRequests);
    }

    [Fact]
    public async Task RunAsync_IncludePinsFalse_IssuesNoPinRequestsEvenWhenSourcePinned()
    {
        var messages = new[]
        {
            MakeMessageJson("10000000000000001", content: "pinned", pinned: true),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, MessagesUrl, messages)
            .RespondJson(HttpMethod.Post, WebhookCreateUrl, MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/" + WebhookId, HttpStatusCode.NoContent)
            .Respond(HttpMethod.Put, PinUrlPrefix, HttpStatusCode.NoContent);
        RegisterWebhookExecutes(handler, 1);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(Options(includePins: false), NoOpProgress(), CancellationToken.None);

        Assert.Equal(1, result.TotalMigrated);

        var pinRequests = handler.SentRequests
            .Where(r => r.Method == HttpMethod.Put &&
                        r.RequestUri!.ToString().Contains(PinUrlPrefix, StringComparison.Ordinal))
            .ToList();
        Assert.Empty(pinRequests);
    }

    // --- Polls ---

    [Fact]
    public async Task RunAsync_MessageWithPoll_WebhookBodyContainsPollField()
    {
        var messages = new[]
        {
            MakeMessageJson(
                "10000000000000001",
                content: "vote!",
                poll: MakePollJson("Favorite color?", "Red", "Blue")),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, MessagesUrl, messages)
            .RespondJson(HttpMethod.Post, WebhookCreateUrl, MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/" + WebhookId, HttpStatusCode.NoContent);
        RegisterWebhookExecutes(handler, 1);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(Options(includePolls: true), NoOpProgress(), CancellationToken.None);

        Assert.Equal(1, result.TotalMigrated);

        var bodies = WebhookExecuteBodies(handler);
        var body = Assert.Single(bodies);
        Assert.Contains("\"poll\"", body, StringComparison.Ordinal);
        Assert.Contains("Favorite color?", body, StringComparison.Ordinal);
        Assert.Contains("Red", body, StringComparison.Ordinal);
        Assert.Contains("Blue", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_PollOnlyMessage_IsMigratedNotSkipped()
    {
        // No content and no attachments — only a poll. Must still be migrated (not skipped/failed).
        var messages = new[]
        {
            MakeMessageJson(
                "10000000000000001",
                content: null,
                poll: MakePollJson("Yes or no?", "Yes", "No")),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, MessagesUrl, messages)
            .RespondJson(HttpMethod.Post, WebhookCreateUrl, MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/" + WebhookId, HttpStatusCode.NoContent);
        RegisterWebhookExecutes(handler, 1);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(Options(includePolls: true), NoOpProgress(), CancellationToken.None);

        Assert.Equal(1, result.TotalMigrated);
        Assert.Equal(0, result.TotalFailed);
        Assert.Equal(1, CountWebhookExecutePosts(handler));
    }

    [Fact]
    public async Task RunAsync_IncludePollsFalse_WebhookBodyHasNoPoll()
    {
        var messages = new[]
        {
            MakeMessageJson(
                "10000000000000001",
                content: "vote!",
                poll: MakePollJson("Favorite color?", "Red", "Blue")),
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, MessagesUrl, messages)
            .RespondJson(HttpMethod.Post, WebhookCreateUrl, MakeWebhookJson())
            .Respond(HttpMethod.Delete, "/webhooks/" + WebhookId, HttpStatusCode.NoContent);
        RegisterWebhookExecutes(handler, 1);

        var engine = CreateEngine(handler);
        var result = await engine.RunAsync(Options(includePolls: false), NoOpProgress(), CancellationToken.None);

        Assert.Equal(1, result.TotalMigrated);

        var bodies = WebhookExecuteBodies(handler);
        var body = Assert.Single(bodies);
        // With polls disabled the payload's poll field serializes to null (the question text is gone).
        Assert.DoesNotContain("Favorite color?", body, StringComparison.Ordinal);
    }
}
