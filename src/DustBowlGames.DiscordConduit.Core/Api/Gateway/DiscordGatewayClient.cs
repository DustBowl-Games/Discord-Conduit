using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Api.Gateway;

/// <summary>
/// WebSocket-based client for the Discord Gateway, handling heartbeats,
/// identification, reconnection, and event dispatching.
/// </summary>
public sealed class DiscordGatewayClient : IDisposable, IAsyncDisposable
{
    private readonly string _botToken;
    private readonly DiscordRestClient _restClient;
    private readonly ILogger _logger;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;

    // Sentinel for "no sequence received yet". Discord sequence numbers are non-negative,
    // so -1 maps back to null in the heartbeat/RESUME payloads.
    private const int NoSequence = -1;

    // Written by the receive loop, read by the heartbeat loop — accessed via Volatile/Interlocked.
    // A Nullable<int> can't be volatile and isn't atomically read/written, so we use a plain int
    // with a sentinel and map it back to null where the protocol requires null.
    private int _lastSequence = NoSequence;
    private string? _sessionId;
    private string? _resumeGatewayUrl;
    // Written by the receive loop (op 11 ACK) and read/written by the heartbeat loop —
    // accessed via Volatile.Read/Write so both threads observe a consistent value.
    private bool _heartbeatAcked = true;
    // The caller-supplied cancellation token captured in ConnectAsync. Reconnects relink to
    // this so the external token can still stop the loops after a reconnect.
    private CancellationToken _externalCt;
    // Set when DisconnectAsync/Dispose has been requested so the reconnect path won't
    // recreate the socket during shutdown.
    private volatile bool _stopping;
    private TaskCompletionSource _readyTcs = new();
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    private static JsonSerializerOptions JsonOptions => Json.CoreJsonOptions.Default;

    /// <summary>
    /// Fires when an INTERACTION_CREATE dispatch event is received from the gateway.
    /// </summary>
    public event Func<InteractionCreateEvent, Task>? OnInteractionCreate;

    /// <summary>
    /// The application ID obtained from the READY event, or null if not yet connected.
    /// </summary>
    public string? ApplicationId { get; private set; }

    /// <summary>
    /// Whether the gateway is connected and the bot has successfully identified.
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Creates a new Discord gateway client.
    /// </summary>
    /// <param name="botToken">The bot token for authentication.</param>
    /// <param name="restClient">REST client used to fetch the gateway URL.</param>
    /// <param name="logger">Logger instance.</param>
    public DiscordGatewayClient(string botToken, DiscordRestClient restClient, ILogger logger)
    {
        _botToken = botToken;
        _restClient = restClient;
        _logger = logger;
    }

    /// <summary>
    /// Connects to the Discord gateway, starts the receive loop, and begins heartbeating.
    /// </summary>
    /// <param name="ct">Cancellation token to abort the connection.</param>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var gatewayInfo = await _restClient.GetAsync<GatewayBotResponse>("/gateway/bot", ct).ConfigureAwait(false);
        var url = $"{gatewayInfo.Url}?v=10&encoding=json";

        // The token is sent immediately after connecting, so refuse to connect to a non-Discord
        // host even if /gateway/bot returns something unexpected.
        if (!IsValidGatewayUrl(url))
        {
            _logger.Warning("Gateway URL failed validation: {Url}", url);
            throw new InvalidOperationException(
                "Discord returned an invalid gateway URL. Refusing to connect.");
        }

        _logger.Information("Connecting to gateway: {Url}", url);

        _readyTcs = new TaskCompletionSource();
        _externalCt = ct;
        _stopping = false;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(url), _cts.Token).ConfigureAwait(false);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);

        // Wait for the READY event so ApplicationId is available when ConnectAsync returns
        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, readyTimeout.Token);
        try
        {
            await _readyTcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (readyTimeout.IsCancellationRequested)
        {
            _logger.Error("Timed out waiting for gateway READY event");
            throw new TimeoutException("Gateway did not send READY within 30 seconds. Check your bot token and intents.");
        }
    }

    /// <summary>
    /// Gracefully disconnects from the gateway, cancelling background tasks.
    /// </summary>
    public async Task DisconnectAsync()
    {
        IsConnected = false;
        // Signal shutdown first so a concurrent reconnect won't recreate the socket
        // after we tear it down.
        _stopping = true;

        // Cancel and capture the tasks/socket under the reconnect lock so we can't
        // interleave destructively with HandleReconnectAsync swapping _ws/_cts.
        ClientWebSocket? ws;
        Task? receiveTask;
        Task? heartbeatTask;
        await _reconnectLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cts is not null)
            {
                await _cts.CancelAsync().ConfigureAwait(false);
            }

            ws = _ws;
            receiveTask = _receiveTask;
            heartbeatTask = _heartbeatTask;
        }
        finally
        {
            _reconnectLock.Release();
        }

        if (ws is { State: WebSocketState.Open })
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", closeCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error during WebSocket close");
            }
        }

        if (receiveTask is not null)
        {
            try { await receiveTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        if (heartbeatTask is not null)
        {
            try { await heartbeatTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        await _reconnectLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _ws?.Dispose();
            _ws = null;
            _cts?.Dispose();
            _cts = null;
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping = true;
        _cts?.Cancel();
        _ws?.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!ct.IsCancellationRequested && _ws is { State: WebSocketState.Open })
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.Warning("Gateway sent close: {Status} {Description}",
                            result.CloseStatus, result.CloseStatusDescription);
                        await HandleReconnectAsync(ct).ConfigureAwait(false);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);

                    if (ms.Length > 4 * 1024 * 1024) // 4 MB sanity cap
                    {
                        _logger.Error("Gateway payload exceeded 4 MB limit, disconnecting");
                        return;
                    }
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

                GatewayPayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<GatewayPayload>(json, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.Warning(ex, "Failed to deserialize gateway payload");
                    continue;
                }

                if (payload is null) continue;

                if (payload.S.HasValue)
                {
                    Volatile.Write(ref _lastSequence, payload.S.Value);
                }

                try
                {
                    await HandlePayloadAsync(payload, ct).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    // Belt-and-suspenders: a malformed dispatch payload must not fault the receive
                    // task (which would leave the bot connected but deaf). Log and keep reading.
                    _logger.Warning(ex, "Failed to handle gateway dispatch payload");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (JsonException ex)
        {
            // Defensive backstop for any JsonException not caught closer to the deserialization
            // site. Log and continue reading rather than faulting the receive loop.
            _logger.Warning(ex, "JSON error in receive loop");
            if (!ct.IsCancellationRequested)
            {
                await HandleReconnectAsync(ct).ConfigureAwait(false);
            }
        }
        catch (WebSocketException ex)
        {
            _logger.Error(ex, "WebSocket error in receive loop");
            if (!ct.IsCancellationRequested)
            {
                await HandleReconnectAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task HandlePayloadAsync(GatewayPayload payload, CancellationToken ct)
    {
        switch (payload.Op)
        {
            case 10: // Hello
                await HandleHelloAsync(payload, ct).ConfigureAwait(false);
                break;

            case 11: // Heartbeat ACK
                Volatile.Write(ref _heartbeatAcked, true);
                break;

            case 0: // Dispatch
                await HandleDispatchAsync(payload, ct).ConfigureAwait(false);
                break;

            case 7: // Reconnect
                _logger.Information("Gateway requested reconnect");
                await HandleReconnectAsync(ct).ConfigureAwait(false);
                break;

            case 9: // Invalid Session
                var resumable = payload.D.HasValue &&
                                payload.D.Value.ValueKind == JsonValueKind.True;
                _logger.Warning("Invalid session (resumable={Resumable})", resumable);
                if (resumable)
                {
                    await SendResumeAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    _sessionId = null;
                    Volatile.Write(ref _lastSequence, NoSequence);
                    await HandleReconnectAsync(ct).ConfigureAwait(false);
                }
                break;
        }
    }

    private async Task HandleHelloAsync(GatewayPayload payload, CancellationToken ct)
    {
        if (!payload.D.HasValue) return;

        var hello = JsonSerializer.Deserialize<HelloData>(payload.D.Value.GetRawText(), JsonOptions);
        if (hello is null) return;

        _logger.Information("Received Hello, heartbeat interval: {Interval}ms", hello.HeartbeatInterval);

        // The server-supplied interval is untrusted. A value of 0 makes the heartbeat loop
        // hot-spin (CPU burn + heartbeat flood) and a negative value throws on Task.Delay, so
        // reject non-positive intervals with a clean reconnect and clamp anything else to a sane
        // range before starting the loop.
        if (hello.HeartbeatInterval <= 0)
        {
            _logger.Warning("Gateway sent invalid heartbeat interval {Interval}ms, reconnecting",
                hello.HeartbeatInterval);
            await HandleReconnectAsync(ct).ConfigureAwait(false);
            return;
        }

        var interval = Math.Clamp(hello.HeartbeatInterval, 1000, 600000);

        Volatile.Write(ref _heartbeatAcked, true);
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(interval, ct), ct);

        if (_sessionId is not null)
        {
            await SendResumeAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await SendIdentifyAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task HandleDispatchAsync(GatewayPayload payload, CancellationToken ct)
    {
        switch (payload.T)
        {
            case "READY":
                if (payload.D.HasValue)
                {
                    // A malformed/missing-required-field READY would throw a JsonException that
                    // propagates into ReceiveLoopAsync and faults the receive task (bot connected
                    // but deaf). Wrap it like INTERACTION_CREATE so a bad payload is logged and the
                    // loop continues.
                    try
                    {
                        var ready = JsonSerializer.Deserialize<ReadyData>(
                            payload.D.Value.GetRawText(), JsonOptions);
                        if (ready is not null)
                        {
                            _sessionId = ready.SessionId;
                            _resumeGatewayUrl = ready.ResumeGatewayUrl;
                            ApplicationId = ready.Application.Id;
                            IsConnected = true;
                            // session_id is a resume credential — log only a short prefix, never
                            // the full value.
                            _logger.Information("Gateway READY, session={SessionId}, app={AppId}",
                                RedactSessionId(_sessionId), ApplicationId);
                            _readyTcs.TrySetResult();
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.Warning(ex, "Failed to deserialize READY payload");
                    }
                }
                break;

            case "RESUMED":
                IsConnected = true;
                _logger.Information("Gateway session resumed");
                break;

            case "INTERACTION_CREATE":
                if (payload.D.HasValue && OnInteractionCreate is not null)
                {
                    try
                    {
                        var interaction = JsonSerializer.Deserialize<InteractionCreateEvent>(
                            payload.D.Value.GetRawText(), JsonOptions);
                        if (interaction is not null)
                        {
                            await OnInteractionCreate.Invoke(interaction).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error handling INTERACTION_CREATE");
                    }
                }
                break;

            default:
                _logger.Debug("Unhandled dispatch event: {EventName}", payload.T);
                break;
        }
    }

    private async Task HeartbeatLoopAsync(int intervalMs, CancellationToken ct)
    {
        try
        {
            // First heartbeat uses jitter. Compute in double/long so the multiplication can't
            // overflow, then clamp the delay into the valid Task.Delay range.
            var jitter = Random.Shared.NextDouble();
            var firstDelay = (long)(intervalMs * jitter);
            firstDelay = Math.Clamp(firstDelay, 0, intervalMs);
            await Task.Delay(TimeSpan.FromMilliseconds(firstDelay), ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                if (!Volatile.Read(ref _heartbeatAcked))
                {
                    _logger.Warning("Heartbeat ACK not received, reconnecting");
                    await HandleReconnectAsync(ct).ConfigureAwait(false);
                    return;
                }

                Volatile.Write(ref _heartbeatAcked, false);
                await SendHeartbeatAsync(ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(intervalMs), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            // Don't let the heartbeat task fault silently — that stops heartbeating with no
            // reconnect and the connection goes zombie. Log and drive the same reconnect path
            // the receive loop uses.
            _logger.Error(ex, "Heartbeat loop failed, attempting reconnect");
            if (!ct.IsCancellationRequested && !_stopping)
            {
                try
                {
                    await HandleReconnectAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down — nothing more to do.
                }
                catch (Exception reconnectEx)
                {
                    _logger.Error(reconnectEx, "Reconnect from heartbeat loop failed");
                }
            }
        }
    }

    private Task SendHeartbeatAsync(CancellationToken ct)
    {
        // Discord's heartbeat "d" is the last sequence number, or null if none received yet.
        int? seq = LastSequenceOrNull();
        var payload = new GatewayPayload { Op = 1, D = SerializeToElement(seq) };
        return SendAsync(payload, ct);
    }

    /// <summary>
    /// Reads the last sequence number atomically, mapping the no-sequence sentinel back to null.
    /// </summary>
    private int? LastSequenceOrNull()
    {
        var seq = Volatile.Read(ref _lastSequence);
        return seq == NoSequence ? null : seq;
    }

    private Task SendIdentifyAsync(CancellationToken ct)
    {
        var identify = new
        {
            token = _botToken,
            intents = (int)(GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent),
            properties = new
            {
                os = OperatingSystem.IsWindows() ? "windows"
                    : OperatingSystem.IsMacOS() ? "macos"
                    : OperatingSystem.IsLinux() ? "linux"
                    : "unknown",
                browser = "DiscordConduit",
                device = "DiscordConduit"
            }
        };

        var payload = new GatewayPayload { Op = 2, D = SerializeToElement(identify) };
        return SendAsync(payload, ct);
    }

    private Task SendResumeAsync(CancellationToken ct)
    {
        var resume = new
        {
            token = _botToken,
            session_id = _sessionId,
            seq = LastSequenceOrNull()
        };

        _logger.Information("Sending RESUME for session {SessionId}", RedactSessionId(_sessionId));
        var payload = new GatewayPayload { Op = 6, D = SerializeToElement(resume) };
        return SendAsync(payload, ct);
    }

    /// <summary>
    /// Redacts a gateway session_id for logging. The session_id is a resume credential, so only a
    /// short prefix is emitted.
    /// </summary>
    private static string RedactSessionId(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return "(none)";
        return sessionId.Length <= 6 ? sessionId + "…" : sessionId[..6] + "…";
    }

    private async Task SendAsync(GatewayPayload payload, CancellationToken ct)
    {
        // Snapshot the socket so a concurrent reconnect that swaps/disposes _ws can't
        // turn this into a use-after-dispose or NRE mid-send.
        var ws = _ws;
        if (ws is not { State: WebSocketState.Open }) return;

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The socket was disposed by a concurrent reconnect/disconnect after our snapshot.
            // The reconnect path owns recovery, so drop this send silently.
        }
    }

    private async Task HandleReconnectAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested || _stopping) return;

        if (!await _reconnectLock.WaitAsync(0).ConfigureAwait(false)) // non-blocking try
        {
            _logger.Debug("Reconnect already in progress, skipping");
            return;
        }
        try
        {
            // We may have raced with DisconnectAsync/Dispose; if shutdown was requested,
            // don't recreate the socket.
            if (_stopping) return;

            IsConnected = false;

            // Cancel old tasks (heartbeat, receive loop) and rebuild the CTS *linked to the
            // external token* so the caller's token can still stop the new loops. The previous
            // implementation created an unlinked CTS here, which severed the external token on
            // every reconnect and leaked the old CTS.
            var oldCts = _cts;
            oldCts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(_externalCt);
            var newCt = _cts.Token;
            oldCts?.Dispose();

            // Swap out the old socket under the lock so SendAsync/ReceiveLoop can't observe a
            // half-disposed _ws. Detach the field first, then close/dispose the local.
            var oldWs = _ws;
            _ws = null;

            if (oldWs is { State: WebSocketState.Open or WebSocketState.CloseReceived })
            {
                try
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await oldWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", closeCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error closing WebSocket during reconnect");
                }
            }

            oldWs?.Dispose();

            // Wait before reconnecting
            await Task.Delay(TimeSpan.FromSeconds(5), newCt).ConfigureAwait(false);

            // Shutdown may have been requested during the backoff delay.
            if (_stopping || newCt.IsCancellationRequested) return;

            var baseUrl = _resumeGatewayUrl;

            // The resume URL comes from an untrusted READY payload. If it doesn't validate as a
            // Discord gateway endpoint, discard it (and the resume attempt) and fall back to a
            // fresh GET /gateway/bot so we never send the token to an attacker-controlled host.
            if (baseUrl is not null && !IsValidGatewayUrl($"{baseUrl}?v=10&encoding=json"))
            {
                _logger.Warning("Resume gateway URL failed validation, falling back to /gateway/bot");
                _resumeGatewayUrl = null;
                _sessionId = null;
                Volatile.Write(ref _lastSequence, NoSequence);
                baseUrl = null;
            }

            if (baseUrl is null)
            {
                var gatewayInfo = await _restClient.GetAsync<GatewayBotResponse>("/gateway/bot", newCt).ConfigureAwait(false);
                baseUrl = gatewayInfo.Url;
            }

            var url = $"{baseUrl}?v=10&encoding=json";

            // Final guard for the freshly-fetched URL as well. Don't throw here — a throw would
            // fault whichever loop drove the reconnect. Leave _ws null and return; the heartbeat
            // watchdog/gateway will retrigger a reconnect later.
            if (!IsValidGatewayUrl(url))
            {
                _logger.Warning("Reconnect gateway URL failed validation, aborting reconnect: {Url}", url);
                return;
            }

            _logger.Information("Reconnecting to gateway: {Url}", url);

            var newWs = new ClientWebSocket();
            await newWs.ConnectAsync(new Uri(url), newCt).ConfigureAwait(false);
            _ws = newWs;

            // The receive loop will handle Hello -> Resume/Identify
            _receiveTask = Task.Run(() => ReceiveLoopAsync(newCt), newCt);
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private static JsonElement SerializeToElement(object? value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Validates a Discord gateway WebSocket URL before connecting. IDENTIFY/RESUME send the bot
    /// token immediately after connecting, so a tampered URL (from /gateway/bot or a READY
    /// resume_gateway_url) could exfiltrate the token off-host. Requires a <c>wss</c> scheme and a
    /// host within Discord's domains.
    /// </summary>
    /// <param name="url">The candidate gateway URL.</param>
    /// <returns><c>true</c> if the URL is a well-formed Discord gateway endpoint.</returns>
    private static bool IsValidGatewayUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            return false;

        var host = uri.Host;
        if (string.IsNullOrEmpty(host))
            return false;

        // Allow Discord's apex domains exactly, and any subdomain under them (e.g.
        // gateway.discord.gg, gateway-us-east1-d.discord.gg).
        foreach (var domain in GatewayDomains)
        {
            if (string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] GatewayDomains =
    [
        "discord.gg",
        "discord.com",
        "discordapp.com"
    ];
}
