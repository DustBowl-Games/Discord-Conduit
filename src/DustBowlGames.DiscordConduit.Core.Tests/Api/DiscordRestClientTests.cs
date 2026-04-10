using System.Net;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Tests.Fixtures;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Tests.Api;

public class DiscordRestClientTests
{
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task GetAsync_DeserializesGuilds()
    {
        var guilds = new[]
        {
            new { id = "111", name = "Test Guild", icon = (string?)null }
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/users/@me/guilds", guilds);

        using var client = CreateClient(handler);
        var result = await client.GetAsync<List<Guild>>("/users/@me/guilds");

        Assert.Single(result);
        Assert.Equal("111", result[0].Id);
        Assert.Equal("Test Guild", result[0].Name);
    }

    [Fact]
    public async Task GetAsync_DeserializesChannels()
    {
        var channels = new[]
        {
            new { id = "222", type = 0, name = "general", parent_id = (string?)null, position = 0 }
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/guilds/111/channels", channels);

        using var client = CreateClient(handler);
        var result = await client.GetAsync<List<Channel>>("/guilds/111/channels");

        Assert.Single(result);
        Assert.Equal("222", result[0].Id);
        Assert.True(result[0].IsTextChannel);
        Assert.False(result[0].IsThread);
    }

    [Fact]
    public async Task GetAsync_DeserializesMessages()
    {
        var messages = new[]
        {
            new
            {
                id = "333",
                channel_id = "222",
                author = new { id = "444", username = "testuser", global_name = "Test User", avatar = "abc123" },
                content = "Hello world",
                timestamp = "2026-04-10T12:00:00+00:00",
                type = 0,
                attachments = Array.Empty<object>(),
                embeds = Array.Empty<object>(),
            }
        };

        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/222/messages", messages);

        using var client = CreateClient(handler);
        var result = await client.GetAsync<List<Message>>("/channels/222/messages?limit=100");

        Assert.Single(result);
        Assert.Equal("333", result[0].Id);
        Assert.Equal("Hello world", result[0].Content);
        Assert.Equal("Test User", result[0].Author.DisplayName);
        Assert.True(result[0].IsRegularMessage);
    }

    [Fact]
    public async Task PostJsonAsync_CreatesWebhook()
    {
        var webhook = new { id = "555", token = "webhook-token", channel_id = "222", type = 1, name = "Discord Conduit Migration" };
        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Post, "/channels/222/webhooks", webhook);

        using var client = CreateClient(handler);
        var result = await client.PostJsonAsync<Webhook>(
            "/channels/222/webhooks",
            new { name = "Discord Conduit Migration" });

        Assert.Equal("555", result.Id);
        Assert.Equal("webhook-token", result.Token);
    }

    [Fact]
    public async Task DeleteAsync_SendsDeleteRequest()
    {
        var handler = new FakeHttpHandler()
            .Respond(HttpMethod.Delete, "/webhooks/555", HttpStatusCode.NoContent);

        using var client = CreateClient(handler);
        await client.DeleteAsync("/webhooks/555");

        Assert.Single(handler.SentRequests);
        Assert.Equal(HttpMethod.Delete, handler.SentRequests[0].Method);
    }

    [Fact]
    public async Task GetAsync_SetsAuthorizationHeader()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/users/@me", new { id = "1", username = "bot", discriminator = "0" });

        using var client = CreateClient(handler);
        await client.GetAsync<User>("/users/@me");

        var authHeader = handler.SentRequests[0].Headers.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Bot", authHeader.Scheme);
        Assert.Equal("fake-bot-token", authHeader.Parameter);
    }

    private DiscordRestClient CreateClient(FakeHttpHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(DiscordRestClient.BaseUrl) };
        return new DiscordRestClient("fake-bot-token", _logger, httpClient: httpClient);
    }
}
