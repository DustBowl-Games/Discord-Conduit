using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Clones a channel (or an entire category of channels) into a destination guild by creating the
/// destination channel(s) and migrating their messages. The destination guild may be a different
/// server than the source (the bot must be a member of both).
/// </summary>
public sealed class ChannelCloner
{
    private readonly ChannelEndpoints _channelEndpoints;
    private readonly MigrationEngine _engine;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new channel cloner.
    /// </summary>
    /// <param name="channelEndpoints">Endpoint class for channel operations.</param>
    /// <param name="engine">The migration engine used to copy messages into the new channel(s).</param>
    /// <param name="logger">Logger instance.</param>
    public ChannelCloner(ChannelEndpoints channelEndpoints, MigrationEngine engine, ILogger logger)
    {
        _channelEndpoints = channelEndpoints;
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new text channel in <paramref name="destGuildId"/> mirroring the source channel's
    /// name, then migrates the source channel's messages into it.
    /// </summary>
    /// <param name="sourceChannelId">The channel to clone.</param>
    /// <param name="destGuildId">The destination guild (may differ from the source guild).</param>
    /// <param name="destParentId">Optional destination category to place the new channel under.</param>
    /// <param name="includeReactions">Whether to migrate reactions.</param>
    /// <param name="dryRun">When <c>true</c>, the channel is still created but messages aren't posted.</param>
    /// <param name="filter">Optional message filter.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The clone result for the channel.</returns>
    public async Task<ChannelCloneResult> CloneChannelAsync(
        string sourceChannelId,
        string destGuildId,
        string? destParentId,
        bool includeReactions,
        bool dryRun,
        MessageFilter? filter,
        IProgress<MigrationProgress> progress,
        CancellationToken ct)
    {
        var source = await _channelEndpoints.GetChannelAsync(sourceChannelId, ct).ConfigureAwait(false);
        var name = source.Name ?? "cloned-channel";

        var newChannel = await _channelEndpoints
            .CreateChannelAsync(destGuildId, name, type: 0, parentId: destParentId, ct)
            .ConfigureAwait(false);
        _logger.Information("Created channel '{Name}' ({Id}) in guild {Guild}", name, newChannel.Id, destGuildId);

        var options = new MigrationOptions(
            SourceChannelId: sourceChannelId,
            DestinationChannelId: newChannel.Id,
            GuildId: destGuildId,
            DryRun: dryRun,
            IncludeReactions: includeReactions,
            Filter: filter);

        var result = await _engine.RunAsync(options, progress, ct).ConfigureAwait(false);
        return new ChannelCloneResult(newChannel.Id, name, result);
    }

    /// <summary>
    /// Creates a new category in <paramref name="destGuildId"/> mirroring the source category, then
    /// clones each of its text channels into it (in position order).
    /// </summary>
    /// <param name="sourceCategoryId">The category channel to clone.</param>
    /// <param name="destGuildId">The destination guild.</param>
    /// <param name="includeReactions">Whether to migrate reactions.</param>
    /// <param name="dryRun">When <c>true</c>, channels are created but messages aren't posted.</param>
    /// <param name="filter">Optional message filter.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The clone result for the category and each child channel.</returns>
    public async Task<CategoryCloneResult> CloneCategoryAsync(
        string sourceCategoryId,
        string destGuildId,
        bool includeReactions,
        bool dryRun,
        MessageFilter? filter,
        IProgress<MigrationProgress> progress,
        CancellationToken ct)
    {
        var sourceCategory = await _channelEndpoints.GetChannelAsync(sourceCategoryId, ct).ConfigureAwait(false);
        if (sourceCategory.GuildId is null)
            throw new InvalidOperationException($"Channel {sourceCategoryId} has no guild; cannot enumerate its children.");

        var allChannels = await _channelEndpoints.GetGuildChannelsAsync(sourceCategory.GuildId, ct).ConfigureAwait(false);
        var children = allChannels
            .Where(c => c.ParentId == sourceCategoryId && c.IsTextChannel)
            .OrderBy(c => c.Position ?? 0)
            .ToList();

        var newCategory = await _channelEndpoints
            .CreateChannelAsync(destGuildId, sourceCategory.Name ?? "cloned-category", type: 4, ct: ct)
            .ConfigureAwait(false);
        _logger.Information("Created category '{Name}' ({Id}) with {Count} text channel(s) to clone",
            sourceCategory.Name, newCategory.Id, children.Count);

        var channelResults = new List<ChannelCloneResult>();
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            var r = await CloneChannelAsync(
                child.Id, destGuildId, newCategory.Id, includeReactions, dryRun, filter, progress, ct)
                .ConfigureAwait(false);
            channelResults.Add(r);
        }

        return new CategoryCloneResult(newCategory.Id, sourceCategory.Name ?? "category", channelResults);
    }
}

/// <summary>The outcome of cloning a single channel.</summary>
/// <param name="NewChannelId">The snowflake ID of the created channel.</param>
/// <param name="Name">The cloned channel's name.</param>
/// <param name="Migration">The migration result for the channel's messages.</param>
public sealed record ChannelCloneResult(string NewChannelId, string Name, MigrationResult Migration);

/// <summary>The outcome of cloning a category and its text channels.</summary>
/// <param name="NewCategoryId">The snowflake ID of the created category.</param>
/// <param name="Name">The cloned category's name.</param>
/// <param name="Channels">The clone result for each child channel.</param>
public sealed record CategoryCloneResult(string NewCategoryId, string Name, IReadOnlyList<ChannelCloneResult> Channels);
