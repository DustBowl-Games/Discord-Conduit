using System.Net;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Tests.Fixtures;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Tests.Migration;

public class ChannelClonerTests : IDisposable
{
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();
    private readonly string _tempDir;

    public ChannelClonerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ClonerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private (ChannelCloner Cloner, FakeHttpHandler Handler) CreateCloner(FakeHttpHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(DiscordRestClient.BaseUrl) };
        var client = new DiscordRestClient("fake-token", _logger, httpClient: httpClient);
        var channelEndpoints = new ChannelEndpoints(client);
        var engine = new MigrationEngine(
            new MessageEndpoints(client),
            new WebhookEndpoints(client),
            new ReactionEndpoints(client),
            channelEndpoints,
            new MessageMigrator(_logger),
            new AttachmentHandler(new HttpClient(handler), _logger),
            _tempDir,
            _logger);
        return (new ChannelCloner(channelEndpoints, engine, _logger), handler);
    }

    [Fact]
    public async Task CloneChannelAsync_CreatesChannelAndMigratesMessages()
    {
        var messages = new[]
        {
            // newest-first (Discord order); the engine reverses to chronological
            MakeMessage("2", "second"),
            MakeMessage("1", "first"),
        };

        var handler = new FakeHttpHandler()
            // Source channel lookup (register the more-specific messages route first so it isn't shadowed)
            .RespondJson(HttpMethod.Get, "/channels/200/messages", messages)
            .RespondJson(HttpMethod.Get, "/channels/200", new { id = "200", type = 0, name = "general", guild_id = "111" })
            // Create the destination channel in the dest guild
            .RespondJson(HttpMethod.Post, "/guilds/999/channels", new { id = "300", type = 0, name = "general" })
            // Migration into the new channel 300
            .RespondJson(HttpMethod.Post, "/channels/300/webhooks", new { id = "wh-9", token = "tok-9", channel_id = "300", type = 1, name = "Conduit Migration" })
            .RespondJson(HttpMethod.Post, "/webhooks/wh-9/tok-9", MakeReposted("r1"))
            .RespondJson(HttpMethod.Post, "/webhooks/wh-9/tok-9", MakeReposted("r2"))
            .Respond(HttpMethod.Delete, "/webhooks/wh-9", HttpStatusCode.NoContent);

        var (cloner, h) = CreateCloner(handler);

        var result = await cloner.CloneChannelAsync(
            sourceChannelId: "200",
            destGuildId: "999",
            destParentId: null,
            includeReactions: false,
            dryRun: false,
            filter: null,
            progress: new Progress<MigrationProgress>(_ => { }),
            ct: CancellationToken.None);

        Assert.Equal("300", result.NewChannelId);
        Assert.Equal("general", result.Name);
        Assert.Equal(2, result.Migration.TotalMigrated);

        // A channel-create call was made against the destination guild.
        Assert.Contains(h.SentRequests, r =>
            r.Method == HttpMethod.Post &&
            (r.RequestUri?.ToString().Contains("/guilds/999/channels") ?? false));
    }

    private static object MakeMessage(string id, string content) => new
    {
        id,
        channel_id = "200",
        author = new { id = "50", username = "alice" },
        content,
        timestamp = "2024-06-15T12:00:00Z",
        type = 0,
        attachments = Array.Empty<object>(),
        embeds = Array.Empty<object>(),
    };

    private static object MakeReposted(string id) => new
    {
        id,
        channel_id = "300",
        author = new { id = "wh-bot", username = "Conduit Migration" },
        content = "migrated",
        timestamp = "2024-06-15T12:00:01Z",
        type = 0,
        attachments = Array.Empty<object>(),
        embeds = Array.Empty<object>(),
    };
}
