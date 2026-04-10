using System.Net;
using System.Text;
using System.Text.Json;

namespace DustBowlGames.DiscordConduit.Core.Tests.Fixtures;

/// <summary>
/// A fake HTTP message handler that returns pre-configured responses based on URL patterns.
/// Used to mock Discord API responses in unit tests.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = [];
    private readonly List<HttpRequestMessage> _sentRequests = [];

    /// <summary>All requests that were sent through this handler.</summary>
    public IReadOnlyList<HttpRequestMessage> SentRequests => _sentRequests;

    /// <summary>
    /// Registers a response for requests matching the given method and URL substring.
    /// </summary>
    public FakeHttpHandler Respond(HttpMethod method, string urlContains, HttpStatusCode statusCode,
        object? jsonBody = null, Dictionary<string, string>? headers = null)
    {
        _routes.Add(new Route(method, urlContains, statusCode, jsonBody, headers));
        return this;
    }

    /// <summary>
    /// Registers a 200 OK response with a JSON body.
    /// </summary>
    public FakeHttpHandler RespondJson(HttpMethod method, string urlContains, object body,
        Dictionary<string, string>? headers = null)
    {
        return Respond(method, urlContains, HttpStatusCode.OK, body, headers);
    }

    /// <summary>
    /// Registers a 429 Too Many Requests response.
    /// </summary>
    public FakeHttpHandler Respond429(HttpMethod method, string urlContains, double retryAfterSeconds,
        bool isGlobal = false)
    {
        var headers = new Dictionary<string, string>
        {
            ["Retry-After"] = retryAfterSeconds.ToString("F1")
        };
        if (isGlobal) headers["X-RateLimit-Global"] = "true";

        var body = new { retry_after = retryAfterSeconds, global = isGlobal };
        return Respond(method, urlContains, HttpStatusCode.TooManyRequests, body, headers);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _sentRequests.Add(request);

        var url = request.RequestUri?.ToString() ?? "";
        var route = _routes.FirstOrDefault(r =>
            r.Method == request.Method && url.Contains(r.UrlContains, StringComparison.OrdinalIgnoreCase));

        if (route is null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No fake route for {request.Method} {url}")
            });
        }

        // Remove one-shot routes (like 429) after first match so retries get different responses
        if (route.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _routes.Remove(route);
        }

        var response = new HttpResponseMessage(route.StatusCode);

        if (route.JsonBody is not null)
        {
            var json = JsonSerializer.Serialize(route.JsonBody);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (route.Headers is not null)
        {
            foreach (var (key, value) in route.Headers)
            {
                response.Headers.TryAddWithoutValidation(key, value);
            }
        }

        // Add default rate limit headers if not already present
        if (!response.Headers.Contains("X-RateLimit-Bucket"))
        {
            response.Headers.TryAddWithoutValidation("X-RateLimit-Bucket", "fake-bucket");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "10");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "1.0");
        }

        return Task.FromResult(response);
    }

    private sealed record Route(
        HttpMethod Method,
        string UrlContains,
        HttpStatusCode StatusCode,
        object? JsonBody,
        Dictionary<string, string>? Headers);
}
