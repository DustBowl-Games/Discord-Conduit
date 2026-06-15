using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Api;

/// <summary>
/// Centralized rate limiter for Discord API requests.
/// Tracks per-bucket and global rate limits using response headers.
/// </summary>
public sealed class RateLimiter
{
    private readonly ConcurrentDictionary<string, BucketState> _buckets = new();
    private readonly ConcurrentDictionary<string, string> _routeToBucket = new();
    // One gate per bucket (or per route before its bucket is known). Held across the
    // admit -> send -> header-update window so two requests to the same bucket can't both
    // see the last token and both fire. Keyed independently from _buckets so unrelated
    // buckets never block each other.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _bucketGates = new();
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    // Global rate limit (reactive backstop): stored as ticks for atomic access via Interlocked.
    private long _globalResetAtTicks = DateTimeOffset.MinValue.UtcTicks;

    // Proactive global limiter: Discord allows ~50 requests/second globally. We keep this
    // conservative and use a sliding-window counter. Set deliberately at the documented cap so
    // normal traffic is never throttled by us — only genuine bursts above the cap wait.
    private const int GlobalRequestsPerSecond = 50;
    private readonly object _globalWindowLock = new();
    private long _globalWindowStartTicks;
    private int _globalWindowCount;

    /// <summary>
    /// Creates a new rate limiter.
    /// </summary>
    /// <param name="logger">Logger for rate limit events.</param>
    /// <param name="timeProvider">Time provider for testability. Pass <c>TimeProvider.System</c> in production.</param>
    public RateLimiter(ILogger logger, TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Executes an HTTP request through the rate limiter.
    /// Waits for bucket availability, sends the request, updates bucket state, and handles 429 retries.
    /// </summary>
    /// <param name="httpClient">The HttpClient to send the request with.</param>
    /// <param name="routeKey">Route key for bucket identification (e.g., "POST:/channels/123/messages").</param>
    /// <param name="requestFactory">Factory that creates the request. Called again on retry so the request isn't disposed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response.</returns>
    public async Task<HttpResponseMessage> ExecuteAsync(
        HttpClient httpClient,
        string routeKey,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct = default)
    {
        // Serialize the admit -> send -> header-update window per bucket so concurrent requests
        // to the same bucket are admitted one at a time and each one sees the previous response's
        // updated remaining/reset state. The gate is released before any long 429 retry delay so
        // unrelated buckets aren't blocked.
        var gate = GetBucketGate(routeKey);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        HttpResponseMessage response;
        try
        {
            await WaitForBucketAsync(routeKey, ct).ConfigureAwait(false);
            await WaitForGlobalAsync(ct).ConfigureAwait(false);

            response = await httpClient.SendAsync(requestFactory(), ct).ConfigureAwait(false);
            UpdateBucketState(routeKey, response);
        }
        finally
        {
            gate.Release();
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = await GetRetryAfterAsync(response, ct).ConfigureAwait(false);
            _logger.Warning("Rate limited on {RouteKey}. Retrying after {RetryAfter:F2}s", routeKey, retryAfter.TotalSeconds);

            if (response.Headers.Contains("X-RateLimit-Global"))
            {
                var newResetTicks = (_timeProvider.GetUtcNow() + retryAfter).UtcTicks;
                Interlocked.Exchange(ref _globalResetAtTicks, newResetTicks);
            }

            // The retry delay is intentionally outside the per-bucket gate so a 429 on one bucket
            // doesn't stall unrelated buckets sharing the limiter.
            await DelayAsync(retryAfter, ct).ConfigureAwait(false);
            response.Dispose();

            // Retry once, re-serializing through the gate so the retried request still cooperates
            // with other in-flight requests to the same bucket.
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WaitForGlobalAsync(ct).ConfigureAwait(false);
                response = await httpClient.SendAsync(requestFactory(), ct).ConfigureAwait(false);
                UpdateBucketState(routeKey, response);
            }
            finally
            {
                gate.Release();
            }
        }

        return response;
    }

    /// <summary>
    /// Derives a route key from an HTTP method and URL path.
    /// Replaces snowflake IDs with placeholders so routes sharing rate limit buckets are grouped.
    /// </summary>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">URL path (e.g., "/api/v10/channels/123456/messages").</param>
    /// <returns>A route key string.</returns>
    public static string GetRouteKey(HttpMethod method, string path)
    {
        // Replace snowflake IDs (digits-only path segments) with {id},
        // but keep the first resource ID to distinguish major parameters
        var segments = path.Split('/');
        var normalized = new List<string>();
        var firstIdKept = false;

        foreach (var segment in segments)
        {
            if (segment.Length > 0 && segment.All(char.IsDigit))
            {
                if (!firstIdKept)
                {
                    // Keep first ID (major parameter — channel/guild ID)
                    normalized.Add(segment);
                    firstIdKept = true;
                }
                else
                {
                    normalized.Add("{id}");
                }
            }
            else
            {
                normalized.Add(segment);
            }
        }

        return $"{method}:{string.Join('/', normalized)}";
    }

    /// <summary>
    /// Gets the per-bucket serialization gate for a route. Prefers the known bucket id so all
    /// routes that share a Discord bucket share one gate; falls back to the route key until the
    /// first response reveals the bucket id. Concurrent first-requests on an unknown route still
    /// share the route-key gate, so they are serialized too.
    /// </summary>
    private SemaphoreSlim GetBucketGate(string routeKey)
    {
        var bucketId = _routeToBucket.GetValueOrDefault(routeKey);
        var gateKey = bucketId is not null ? "bucket:" + bucketId : "route:" + routeKey;
        return _bucketGates.GetOrAdd(gateKey, static _ => new SemaphoreSlim(1, 1));
    }

    private async Task WaitForBucketAsync(string routeKey, CancellationToken ct)
    {
        var bucketId = _routeToBucket.GetValueOrDefault(routeKey);
        if (bucketId is null) return;

        if (_buckets.TryGetValue(bucketId, out var state) && state.Remaining <= 0)
        {
            var now = _timeProvider.GetUtcNow();
            var delay = state.ResetsAt - now;
            if (delay > TimeSpan.Zero)
            {
                _logger.Debug("Waiting {Delay:F2}s for bucket {Bucket} to reset", delay.TotalSeconds, bucketId);
                await DelayAsync(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task WaitForGlobalAsync(CancellationToken ct)
    {
        // Reactive backstop: honor a global reset deadline learned from a 429 X-RateLimit-Global.
        var now = _timeProvider.GetUtcNow();
        var globalResetAt = new DateTimeOffset(Interlocked.Read(ref _globalResetAtTicks), TimeSpan.Zero);
        var delay = globalResetAt - now;
        if (delay > TimeSpan.Zero)
        {
            _logger.Debug("Waiting {Delay:F2}s for global rate limit reset", delay.TotalSeconds);
            await DelayAsync(delay, ct).ConfigureAwait(false);
        }

        // Proactive limiter: admit up to GlobalRequestsPerSecond per rolling 1-second window.
        // Only genuine bursts above the cap wait; normal traffic passes through untouched.
        await WaitForGlobalWindowAsync(ct).ConfigureAwait(false);
    }

    private async Task WaitForGlobalWindowAsync(CancellationToken ct)
    {
        while (true)
        {
            TimeSpan waitFor;
            lock (_globalWindowLock)
            {
                var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
                var windowLengthTicks = TimeSpan.FromSeconds(1).Ticks;

                // Start (or roll over to) the current window.
                if (_globalWindowStartTicks == 0 || nowTicks - _globalWindowStartTicks >= windowLengthTicks)
                {
                    _globalWindowStartTicks = nowTicks;
                    _globalWindowCount = 0;
                }

                if (_globalWindowCount < GlobalRequestsPerSecond)
                {
                    // Budget available — reserve a slot and proceed without waiting.
                    _globalWindowCount++;
                    return;
                }

                // Budget exhausted for this window: wait until it rolls over, then re-evaluate.
                var elapsedTicks = nowTicks - _globalWindowStartTicks;
                waitFor = TimeSpan.FromTicks(Math.Max(windowLengthTicks - elapsedTicks, 1));
            }

            _logger.Debug("Proactive global limit reached, waiting {Delay:F2}s", waitFor.TotalSeconds);
            await DelayAsync(waitFor, ct).ConfigureAwait(false);
        }
    }

    private void UpdateBucketState(string routeKey, HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Bucket", out var bucketValues))
        {
            var bucketId = bucketValues.First();
            _routeToBucket[routeKey] = bucketId;

            int remaining = 1;
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
            {
                int.TryParse(remainingValues.First(), out remaining);
            }

            var resetAfter = TimeSpan.Zero;
            if (response.Headers.TryGetValues("X-RateLimit-Reset-After", out var resetValues))
            {
                if (double.TryParse(resetValues.First(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                {
                    resetAfter = TimeSpan.FromSeconds(seconds);
                }
            }

            _buckets[bucketId] = new BucketState(remaining, _timeProvider.GetUtcNow() + resetAfter);
        }
    }

    private static async Task<TimeSpan> GetRetryAfterAsync(HttpResponseMessage response, CancellationToken ct)
    {
        // Try the Retry-After header first
        if (response.Headers.TryGetValues("Retry-After", out var retryValues))
        {
            if (double.TryParse(retryValues.First(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(Math.Min(seconds, 60));
            }
        }

        // Fall back to the response body
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("retry_after", out var retryAfterProp))
            {
                return TimeSpan.FromSeconds(Math.Min(retryAfterProp.GetDouble(), 60));
            }
        }
        catch
        {
            // Ignored — fall through to default
        }

        return TimeSpan.FromSeconds(5); // Conservative default
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);
    }

    private sealed record BucketState(int Remaining, DateTimeOffset ResetsAt);
}
