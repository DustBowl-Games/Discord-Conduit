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
    private readonly List<SentRequest> _sentRequestsWithBody = [];

    /// <summary>All requests that were sent through this handler.</summary>
    public IReadOnlyList<HttpRequestMessage> SentRequests => _sentRequests;

    /// <summary>
    /// All requests that were sent through this handler, paired with their request body captured at
    /// send time. (The body is read here because <see cref="HttpClient"/> disposes request content
    /// after sending, so it is no longer readable from <see cref="SentRequests"/> afterwards.)
    /// </summary>
    public IReadOnlyList<SentRequest> SentRequestsWithBody => _sentRequestsWithBody;

    /// <summary>A captured request and its body string (empty when the request had no content).</summary>
    public sealed record SentRequest(HttpMethod Method, string Url, string Body);

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

        // Capture the request body now — HttpClient disposes request content after the send returns,
        // so reading it later from SentRequests would fail. Reading synchronously here is safe for
        // the buffered JsonContent/StringContent the production code uses; any read failure is
        // swallowed so body capture can never affect a test that does not inspect bodies.
        string body = "";
        if (request.Content is not null)
        {
            try { body = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult(); }
            catch { /* leave body empty if the content cannot be read as a string */ }
        }
        _sentRequestsWithBody.Add(new SentRequest(request.Method, url, body));

        // Match by method + URL substring (substring matching is query-string-aware, e.g.
        // "/webhooks/{id}/{token}" matches "/webhooks/{id}/{token}?wait=true"). When several
        // routes are registered for the same match, they are returned in registration order
        // across successive calls (so r1, r2, r3 sequential responses work, and a 429 followed
        // by a success is returned in that order). Once all matches are consumed, the last one
        // is reused, so single-registration routes called many times keep working.
        var matching = _routes
            .Where(r => r.Method == request.Method &&
                        url.Contains(r.UrlContains, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matching.Count == 0)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No fake route for {request.Method} {url}")
            });
        }

        var route = matching.FirstOrDefault(r => !r.Consumed) ?? matching[^1];
        route.Consumed = true;

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
        Dictionary<string, string>? Headers)
    {
        /// <summary>Whether this registration has already served a response (for sequential routes).</summary>
        public bool Consumed { get; set; }
    }
}
