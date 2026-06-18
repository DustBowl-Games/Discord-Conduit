using System.Collections.Concurrent;

namespace DustBowlGames.DiscordConduit.Core.Commands;

/// <summary>Tracks state for multi-step move interactions.</summary>
public sealed class InteractionSession
{
    /// <summary>
    /// A unique per-flow nonce embedded in every component custom_id this session emits.
    /// Used to ensure that a button click from a stale ephemeral message cannot resolve
    /// to a newer session for the same user+guild.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>The ID of the user who initiated the interaction.</summary>
    public required string UserId { get; init; }

    /// <summary>The guild in which the interaction was initiated.</summary>
    public required string GuildId { get; init; }

    /// <summary>The channel from which messages are being moved.</summary>
    public required string SourceChannelId { get; init; }

    /// <summary>The ID of the target message (context menu origin), if any.</summary>
    public string? TargetMessageId { get; init; }

    /// <summary>Number of messages to move (-1 means all from the target message downward).</summary>
    public int MessageCount { get; set; }

    /// <summary>The end message ID for range-based moves.</summary>
    public string? EndMessageId { get; set; }

    /// <summary>The selected action: "channel", "thread", "as_thread", or "as_forum".</summary>
    public string? Action { get; set; }

    /// <summary>The destination channel or thread ID.</summary>
    public string? DestinationId { get; set; }

    /// <summary>
    /// The IDs of the messages that were moved, kept for potential deletion of the originals.
    /// Only IDs are retained — the source channel is already stored on the session.
    /// </summary>
    public List<string>? MovedMessageIds { get; set; }

    /// <summary>
    /// The IDs of the reposted (destination) messages, kept so the move can be undone by deleting
    /// the copies it created. These live in <see cref="PostedChannelId"/>.
    /// </summary>
    public List<string>? RepostedMessageIds { get; set; }

    /// <summary>The channel (or thread) the reposted messages were posted into.</summary>
    public string? PostedChannelId { get; set; }

    /// <summary>When this session was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>In-memory store for interaction sessions keyed by userId:guildId.</summary>
public sealed class InteractionStateStore : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, InteractionSession> _sessions = new();
    private readonly Timer _sweepTimer;
    private bool _disposed;

    /// <summary>Initializes the store and starts a background sweep timer to purge expired sessions.</summary>
    public InteractionStateStore()
    {
        // Fire roughly once a minute to purge sessions older than the TTL.
        _sweepTimer = new Timer(_ => PurgeExpired(), null, SweepInterval, SweepInterval);
    }

    /// <summary>Builds a session key from a user ID and guild ID.</summary>
    /// <param name="userId">The user's snowflake ID.</param>
    /// <param name="guildId">The guild's snowflake ID.</param>
    /// <returns>A composite key in the format "userId:guildId".</returns>
    public static string Key(string userId, string guildId) => $"{userId}:{guildId}";

    /// <summary>Stores or replaces a session.</summary>
    /// <param name="key">The session key.</param>
    /// <param name="session">The session to store.</param>
    public void Set(string key, InteractionSession session)
    {
        _sessions[key] = session;
    }

    /// <summary>Retrieves a session by key, or null if not found or expired.</summary>
    /// <param name="key">The session key.</param>
    /// <returns>The session if found and not expired; otherwise <c>null</c>.</returns>
    public InteractionSession? Get(string key)
    {
        if (!_sessions.TryGetValue(key, out var session))
            return null;

        if (IsExpired(session))
        {
            _sessions.TryRemove(key, out _);
            return null;
        }

        return session;
    }

    /// <summary>Removes a session by key.</summary>
    /// <param name="key">The session key.</param>
    public void Remove(string key) => _sessions.TryRemove(key, out _);

    /// <summary>Disposes the background sweep timer.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sweepTimer.Dispose();
    }

    private static bool IsExpired(InteractionSession session) =>
        session.CreatedAt < DateTimeOffset.UtcNow - Ttl;

    private void PurgeExpired()
    {
        foreach (var kvp in _sessions)
        {
            if (IsExpired(kvp.Value))
                _sessions.TryRemove(kvp.Key, out _);
        }
    }
}
