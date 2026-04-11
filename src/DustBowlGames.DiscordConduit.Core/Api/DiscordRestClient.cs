using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Api;

/// <summary>
/// Thin wrapper over HttpClient for Discord REST API calls.
/// All requests are routed through the centralized <see cref="RateLimiter"/>.
/// </summary>
public sealed class DiscordRestClient : IDisposable
{
    /// <summary>Discord API base URL.</summary>
    /// <summary>Discord API base URL.</summary>
    public const string BaseUrl = "https://discord.com/api/v10";

    private readonly HttpClient _httpClient;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger _logger;
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates a new Discord REST client.
    /// </summary>
    /// <param name="botToken">The bot token for authentication.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Time provider for rate limiter testability.</param>
    /// <param name="httpClient">Optional HttpClient for testing. If not provided, a new one is created.</param>
    public DiscordRestClient(string botToken, ILogger logger, TimeProvider? timeProvider = null, HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        // Don't use BaseAddress — relative URI resolution with paths is unreliable.
        // Instead, BuildUrl() prepends the base URL to all paths.
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", botToken);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "DiscordConduit (https://github.com/DustBowlGames/discord-conduit, 1.0)");

        _rateLimiter = new RateLimiter(logger, timeProvider);
        _logger = logger;
    }

    /// <summary>Constructs a full URL from a relative API path.</summary>
    private static string BuildUrl(string path) => $"{BaseUrl}{path}";

    /// <summary>Ensures success, logging the response body on failure for debugging.</summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, string method, string path)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.Error("Discord API error: {Method} {Path} -> {Status}: {Body}",
                method, path, (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode(); // throws HttpRequestException on non-2xx
        }
    }

    /// <summary>
    /// Sends a GET request to the Discord API.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response body into.</typeparam>
    /// <param name="path">API path relative to the base URL (e.g., "/guilds/123").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var routeKey = RateLimiter.GetRouteKey(HttpMethod.Get, path);
        var response = await _rateLimiter.ExecuteAsync(
            _httpClient,
            routeKey,
            () => new HttpRequestMessage(HttpMethod.Get, BuildUrl(path)),
            ct);

        await EnsureSuccessAsync(response, "API", "request");
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException($"Null response from GET {path}");
    }

    /// <summary>
    /// Sends a POST request with a JSON body.
    /// </summary>
    /// <typeparam name="TResponse">The type to deserialize the response body into.</typeparam>
    /// <param name="path">API path.</param>
    /// <param name="body">The object to serialize as JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    public async Task<TResponse> PostJsonAsync<TResponse>(string path, object body, CancellationToken ct = default)
    {
        var routeKey = RateLimiter.GetRouteKey(HttpMethod.Post, path);
        var response = await _rateLimiter.ExecuteAsync(
            _httpClient,
            routeKey,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
                request.Content = JsonContent.Create(body, options: JsonOptions);
                return request;
            },
            ct);

        await EnsureSuccessAsync(response, "API", "request");
        var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException($"Null response from POST {path}");
    }

    /// <summary>
    /// Sends a POST request with multipart form data (for file uploads).
    /// Accepts a factory so fresh content can be built on 429 retries (HttpContent is consumed after send).
    /// </summary>
    /// <typeparam name="TResponse">The type to deserialize the response body into.</typeparam>
    /// <param name="path">API path.</param>
    /// <param name="contentFactory">Factory that creates fresh multipart content for each attempt.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    public async Task<TResponse> PostMultipartAsync<TResponse>(string path, Func<MultipartFormDataContent> contentFactory, CancellationToken ct = default)
    {
        var routeKey = RateLimiter.GetRouteKey(HttpMethod.Post, path);
        var response = await _rateLimiter.ExecuteAsync(
            _httpClient,
            routeKey,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
                request.Content = contentFactory();
                return request;
            },
            ct);

        await EnsureSuccessAsync(response, "API", "request");
        var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException($"Null response from POST {path}");
    }

    /// <summary>
    /// Sends a POST request with a JSON body, expecting no response body (204 No Content).
    /// </summary>
    /// <param name="path">API path.</param>
    /// <param name="body">The object to serialize as JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task PostJsonNoResponseAsync(string path, object body, CancellationToken ct = default)
    {
        var routeKey = RateLimiter.GetRouteKey(HttpMethod.Post, path);
        var response = await _rateLimiter.ExecuteAsync(
            _httpClient,
            routeKey,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
                request.Content = JsonContent.Create(body, options: JsonOptions);
                return request;
            },
            ct);

        await EnsureSuccessAsync(response, "API", "request");
    }

    /// <summary>
    /// Sends a PUT request with a JSON body.
    /// </summary>
    /// <typeparam name="TResponse">The type to deserialize the response body into.</typeparam>
    /// <param name="path">API path.</param>
    /// <param name="body">The object to serialize as JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    public async Task<TResponse> PutJsonAsync<TResponse>(string path, object body, CancellationToken ct = default)
    {
        var routeKey = RateLimiter.GetRouteKey(HttpMethod.Put, path);
        var response = await _rateLimiter.ExecuteAsync(
            _httpClient,
            routeKey,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(path));
                request.Content = JsonContent.Create(body, options: JsonOptions);
                return request;
            },
            ct);

        await EnsureSuccessAsync(response, "API", "request");
        var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException($"Null response from PUT {path}");
    }

    /// <summary>
    /// Sends a PATCH request with a JSON body.
    /// </summary>
    /// <typeparam name="TResponse">The type to deserialize the response body into.</typeparam>
    /// <param name="path">API path.</param>
    /// <param name="body">The object to serialize as JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    public async Task<TResponse> PatchJsonAsync<TResponse>(string path, object body, CancellationToken ct = default)
    {
        var routeKey = RateLimiter.GetRouteKey(HttpMethod.Patch, path);
        var response = await _rateLimiter.ExecuteAsync(
            _httpClient,
            routeKey,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Patch, BuildUrl(path));
                request.Content = JsonContent.Create(body, options: JsonOptions);
                return request;
            },
            ct);

        await EnsureSuccessAsync(response, "API", "request");
        var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException($"Null response from PATCH {path}");
    }

    /// <summary>
    /// Sends a PUT request (used for reactions).
    /// </summary>
    /// <param name="path">API path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task PutAsync(string path, CancellationToken ct = default)
    {
        var routeKey = RateLimiter.GetRouteKey(HttpMethod.Put, path);
        var response = await _rateLimiter.ExecuteAsync(
            _httpClient,
            routeKey,
            () => new HttpRequestMessage(HttpMethod.Put, BuildUrl(path)),
            ct);

        await EnsureSuccessAsync(response, "API", "request");
    }

    /// <summary>
    /// Sends a DELETE request.
    /// </summary>
    /// <param name="path">API path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var routeKey = RateLimiter.GetRouteKey(HttpMethod.Delete, path);
        var response = await _rateLimiter.ExecuteAsync(
            _httpClient,
            routeKey,
            () => new HttpRequestMessage(HttpMethod.Delete, BuildUrl(path)),
            ct);

        await EnsureSuccessAsync(response, "API", "request");
    }

    /// <summary>
    /// Sends a raw request through the rate limiter.
    /// Use this for non-standard requests or when you need direct access to the response.
    /// </summary>
    /// <param name="requestFactory">Factory to create the request.</param>
    /// <param name="routeKey">Route key for rate limiting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw HTTP response.</returns>
    public Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, string routeKey, CancellationToken ct = default)
    {
        return _rateLimiter.ExecuteAsync(_httpClient, routeKey, requestFactory, ct);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
