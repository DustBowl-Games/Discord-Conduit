using System.Collections.Concurrent;

namespace DustBowlGames.DiscordConduit.Core.Commands;

/// <summary>Tracks state for multi-step move interactions.</summary>
public sealed class InteractionSession
{
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

    /// <summary>The messages that were fetched and moved, kept for potential deletion.</summary>
    public List<Api.Models.Message>? MovedMessages { get; set; }

    /// <summary>When this session was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>In-memory store for interaction sessions keyed by userId:guildId.</summary>
public sealed class InteractionStateStore
{
    private readonly ConcurrentDictionary<string, InteractionSession> _sessions = new();

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
        PurgeExpired();
        _sessions[key] = session;
    }

    /// <summary>Retrieves a session by key, or null if not found.</summary>
    /// <param name="key">The session key.</param>
    /// <returns>The session if found; otherwise <c>null</c>.</returns>
    public InteractionSession? Get(string key)
    {
        PurgeExpired();
        return _sessions.GetValueOrDefault(key);
    }

    /// <summary>Removes a session by key.</summary>
    /// <param name="key">The session key.</param>
    public void Remove(string key) => _sessions.TryRemove(key, out _);

    private void PurgeExpired()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.CreatedAt < cutoff)
                _sessions.TryRemove(kvp.Key, out _);
        }
    }
}
