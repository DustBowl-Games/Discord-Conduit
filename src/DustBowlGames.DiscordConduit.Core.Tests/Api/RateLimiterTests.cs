using System.Net;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Tests.Fixtures;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Tests.Api;

public class RateLimiterTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task ExecuteAsync_NormalRequest_ReturnsResponse()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(HttpMethod.Get, "/channels/123", new { id = "123", name = "test" });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://discord.com/api/v10") };
        var limiter = new RateLimiter(_logger, _time);

        var response = await limiter.ExecuteAsync(
            client,
            "GET:/channels/123",
            () => new HttpRequestMessage(HttpMethod.Get, "/channels/123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_429Response_RetriesAfterDelay()
    {
        var handler = new FakeHttpHandler()
            .Respond429(HttpMethod.Get, "/channels/123", 1.0)
            .RespondJson(HttpMethod.Get, "/channels/123", new { id = "123", name = "test" });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://discord.com/api/v10") };
        var limiter = new RateLimiter(_logger, _time);

        // Start the request without awaiting so we can advance the fake clock past the
        // retry-after delay (Task.Delay runs on the injected TimeProvider).
        var task = limiter.ExecuteAsync(
            client,
            "GET:/channels/123",
            () => new HttpRequestMessage(HttpMethod.Get, "/channels/123"));

        // Advance past the 1s retry-after so the retry fires.
        _time.Advance(TimeSpan.FromSeconds(2));
        var response = await task;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.SentRequests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_BucketExhausted_WaitsForReset()
    {
        // First request sets remaining=0 with 2s reset
        var headers = new Dictionary<string, string>
        {
            ["X-RateLimit-Bucket"] = "test-bucket",
            ["X-RateLimit-Remaining"] = "0",
            ["X-RateLimit-Reset-After"] = "2.0"
        };
        var handler = new FakeHttpHandler()
            .Respond(HttpMethod.Get, "/channels/123", HttpStatusCode.OK,
                new { id = "123" }, headers)
            .RespondJson(HttpMethod.Get, "/channels/123", new { id = "123", name = "test" });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://discord.com/api/v10") };
        var limiter = new RateLimiter(_logger, _time);

        // First request — populates bucket state
        await limiter.ExecuteAsync(client, "GET:/channels/123",
            () => new HttpRequestMessage(HttpMethod.Get, "/channels/123"));

        // Second request — should wait for bucket reset
        var task = limiter.ExecuteAsync(client, "GET:/channels/123",
            () => new HttpRequestMessage(HttpMethod.Get, "/channels/123"));

        // Advance time past the reset
        _time.Advance(TimeSpan.FromSeconds(3));
        var response = await task;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v10/channels/123456/messages", "GET:/api/v10/channels/123456/messages")]
    [InlineData("POST", "/api/v10/channels/123456/messages", "POST:/api/v10/channels/123456/messages")]
    [InlineData("PUT", "/api/v10/channels/123456/messages/789/reactions/emoji/@me", "PUT:/api/v10/channels/123456/messages/{id}/reactions/emoji/@me")]
    public void GetRouteKey_GroupsRoutesCorrectly(string method, string path, string expected)
    {
        var httpMethod = new HttpMethod(method);
        var result = RateLimiter.GetRouteKey(httpMethod, path);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetRouteKey_RedactsWebhookTokenAndQueryString()
    {
        // Webhook tokens are not digit-only, so without redaction they would survive into the
        // route key — which gets logged and used as a dictionary key.
        var key = RateLimiter.GetRouteKey(
            HttpMethod.Post, "/webhooks/123456/SECRETtoken_abc-DEF.xyz?wait=true");

        Assert.DoesNotContain("SECRETtoken", key);
        Assert.DoesNotContain("wait=true", key);
        Assert.Contains("123456", key); // webhook id is preserved for correct per-webhook bucketing
    }

    [Fact]
    public void GetRouteKey_RedactsInteractionToken()
    {
        var key = RateLimiter.GetRouteKey(
            HttpMethod.Post, "/interactions/789012/SUPERsecret.interaction-token/callback");

        Assert.DoesNotContain("SUPERsecret", key);
        Assert.Contains("789012", key);
    }
}
