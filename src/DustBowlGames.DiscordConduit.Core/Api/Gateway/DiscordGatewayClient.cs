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
public sealed class DiscordGatewayClient : IDisposable
{
    private readonly string _botToken;
    private readonly DiscordRestClient _restClient;
    private readonly ILogger _logger;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;

    private int? _lastSequence;
    private string? _sessionId;
    private string? _resumeGatewayUrl;
    private bool _heartbeatAcked = true;
    private TaskCompletionSource _readyTcs = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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
        var gatewayInfo = await _restClient.GetAsync<GatewayBotResponse>("/gateway/bot", ct);
        var url = $"{gatewayInfo.Url}?v=10&encoding=json";

        _logger.Information("Connecting to gateway: {Url}", url);

        _readyTcs = new TaskCompletionSource();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(url), _cts.Token);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);

        // Wait for the READY event so ApplicationId is available when ConnectAsync returns
        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, readyTimeout.Token);
        try
        {
            await _readyTcs.Task.WaitAsync(linked.Token);
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

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", closeCts.Token);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error during WebSocket close");
            }
        }

        if (_receiveTask is not null)
        {
            try { await _receiveTask; } catch (OperationCanceledException) { }
        }

        if (_heartbeatTask is not null)
        {
            try { await _heartbeatTask; } catch (OperationCanceledException) { }
        }

        _ws?.Dispose();
        _ws = null;
        _cts?.Dispose();
        _cts = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
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
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.Warning("Gateway sent close: {Status} {Description}",
                            result.CloseStatus, result.CloseStatusDescription);
                        await HandleReconnectAsync(ct);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
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
                    _lastSequence = payload.S.Value;
                }

                await HandlePayloadAsync(payload, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (WebSocketException ex)
        {
            _logger.Error(ex, "WebSocket error in receive loop");
            if (!ct.IsCancellationRequested)
            {
                await HandleReconnectAsync(ct);
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
                await HandleHelloAsync(payload, ct);
                break;

            case 11: // Heartbeat ACK
                _heartbeatAcked = true;
                break;

            case 0: // Dispatch
                await HandleDispatchAsync(payload, ct);
                break;

            case 7: // Reconnect
                _logger.Information("Gateway requested reconnect");
                await HandleReconnectAsync(ct);
                break;

            case 9: // Invalid Session
                var resumable = payload.D.HasValue &&
                                payload.D.Value.ValueKind == JsonValueKind.True;
                _logger.Warning("Invalid session (resumable={Resumable})", resumable);
                if (resumable)
                {
                    await SendResumeAsync(ct);
                }
                else
                {
                    _sessionId = null;
                    _lastSequence = null;
                    await HandleReconnectAsync(ct);
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

        _heartbeatAcked = true;
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(hello.HeartbeatInterval, ct), ct);

        if (_sessionId is not null)
        {
            await SendResumeAsync(ct);
        }
        else
        {
            await SendIdentifyAsync(ct);
        }
    }

    private async Task HandleDispatchAsync(GatewayPayload payload, CancellationToken ct)
    {
        switch (payload.T)
        {
            case "READY":
                if (payload.D.HasValue)
                {
                    var ready = JsonSerializer.Deserialize<ReadyData>(
                        payload.D.Value.GetRawText(), JsonOptions);
                    if (ready is not null)
                    {
                        _sessionId = ready.SessionId;
                        _resumeGatewayUrl = ready.ResumeGatewayUrl;
                        ApplicationId = ready.Application.Id;
                        IsConnected = true;
                        _logger.Information("Gateway READY, session={SessionId}, app={AppId}",
                            _sessionId, ApplicationId);
                        _readyTcs.TrySetResult();
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
                            await OnInteractionCreate.Invoke(interaction);
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
            // First heartbeat uses jitter
            var jitter = Random.Shared.NextDouble();
            await Task.Delay((int)(intervalMs * jitter), ct);

            while (!ct.IsCancellationRequested)
            {
                if (!_heartbeatAcked)
                {
                    _logger.Warning("Heartbeat ACK not received, reconnecting");
                    await HandleReconnectAsync(ct);
                    return;
                }

                _heartbeatAcked = false;
                await SendHeartbeatAsync(ct);
                await Task.Delay(intervalMs, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    private Task SendHeartbeatAsync(CancellationToken ct)
    {
        var payload = new GatewayPayload { Op = 1, D = SerializeToElement(_lastSequence) };
        return SendAsync(payload, ct);
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
            seq = _lastSequence
        };

        _logger.Information("Sending RESUME for session {SessionId}", _sessionId);
        var payload = new GatewayPayload { Op = 6, D = SerializeToElement(resume) };
        return SendAsync(payload, ct);
    }

    private async Task SendAsync(GatewayPayload payload, CancellationToken ct)
    {
        if (_ws is not { State: WebSocketState.Open }) return;

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct);
    }

    private async Task HandleReconnectAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        IsConnected = false;

        if (_ws is { State: WebSocketState.Open or WebSocketState.CloseReceived })
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", closeCts.Token);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error closing WebSocket during reconnect");
            }
        }

        _ws?.Dispose();

        // Wait before reconnecting
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        var baseUrl = _resumeGatewayUrl;
        if (baseUrl is null)
        {
            var gatewayInfo = await _restClient.GetAsync<GatewayBotResponse>("/gateway/bot", ct);
            baseUrl = gatewayInfo.Url;
        }

        var url = $"{baseUrl}?v=10&encoding=json";

        _logger.Information("Reconnecting to gateway: {Url}", url);

        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(url), ct);

        // The receive loop will handle Hello -> Resume/Identify
        _ = Task.Run(() => ReceiveLoopAsync(ct), ct);
    }

    private static JsonElement SerializeToElement(object? value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
