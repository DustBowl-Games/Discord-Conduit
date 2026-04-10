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

        var response = await limiter.ExecuteAsync(
            client,
            "GET:/channels/123",
            () => new HttpRequestMessage(HttpMethod.Get, "/channels/123"));

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
}
