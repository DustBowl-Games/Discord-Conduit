using System.Diagnostics;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using Serilog;

namespace DustBowlGames.DiscordConduit.Core.Migration;

/// <summary>
/// Orchestrates the full migration of messages from one Discord channel to another,
/// coordinating webhook creation, message pagination, attachment handling, and reaction migration.
/// </summary>
public sealed class MigrationEngine
{
    private readonly MessageEndpoints _messageEndpoints;
    private readonly WebhookEndpoints _webhookEndpoints;
    private readonly ReactionEndpoints _reactionEndpoints;
    private readonly ChannelEndpoints _channelEndpoints;
    private readonly MessageMigrator _messageMigrator;
    private readonly AttachmentHandler _attachmentHandler;
    private readonly string _appDataPath;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new migration engine.
    /// </summary>
    /// <param name="messageEndpoints">Endpoint class for fetching messages.</param>
    /// <param name="webhookEndpoints">Endpoint class for creating and executing webhooks.</param>
    /// <param name="reactionEndpoints">Endpoint class for adding reactions.</param>
    /// <param name="channelEndpoints">Endpoint class for channel operations.</param>
    /// <param name="messageMigrator">Helper for building webhook message content.</param>
    /// <param name="attachmentHandler">Helper for downloading and re-uploading attachments.</param>
    /// <param name="appDataPath">The application data directory for persisting migration state.</param>
    /// <param name="logger">Logger instance.</param>
    public MigrationEngine(
        MessageEndpoints messageEndpoints,
        WebhookEndpoints webhookEndpoints,
        ReactionEndpoints reactionEndpoints,
        ChannelEndpoints channelEndpoints,
        MessageMigrator messageMigrator,
        AttachmentHandler attachmentHandler,
        string appDataPath,
        ILogger logger)
    {
        _messageEndpoints = messageEndpoints;
        _webhookEndpoints = webhookEndpoints;
        _reactionEndpoints = reactionEndpoints;
        _channelEndpoints = channelEndpoints;
        _messageMigrator = messageMigrator;
        _attachmentHandler = attachmentHandler;
        _appDataPath = appDataPath;
        _logger = logger;
    }

    /// <summary>
    /// Performs a pre-flight analysis of the source channel, counting messages,
    /// attachments, oversized files, and estimating the migration duration.
    /// </summary>
    /// <param name="options">The migration options specifying source and destination channels.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="MigrationPreview"/> summarizing what the migration will entail.</returns>
    public async Task<MigrationPreview> PreviewAsync(MigrationOptions options, CancellationToken ct)
    {
        _logger.Information("Starting migration preview for channel {SourceChannelId}", options.SourceChannelId);

        var allMessages = await FetchAllMessagesDescendingAsync(options.SourceChannelId, ct);

        var messageCount = allMessages.Count;
        var attachmentCount = 0;
        long totalAttachmentBytes = 0;
        var oversizedAttachments = new List<OversizedAttachment>();
        var warnings = new List<string>();
        var reactionCount = 0;

        foreach (var message in allMessages)
        {
            if (message.Attachments is { Count: > 0 })
            {
                foreach (var attachment in message.Attachments)
                {
                    attachmentCount++;
                    totalAttachmentBytes += attachment.Size;

                    if (_attachmentHandler.IsOversized(attachment))
                    {
                        oversizedAttachments.Add(new OversizedAttachment(
                            message.Id,
                            attachment.Filename,
                            attachment.Size));
                    }
                }
            }

            if (options.IncludeReactions && message.Reactions is { Count: > 0 })
            {
                foreach (var reaction in message.Reactions)
                {
                    reactionCount += reaction.Count;
                }
            }
        }

        if (oversizedAttachments.Count > 0)
        {
            warnings.Add($"{oversizedAttachments.Count} attachment(s) exceed the 8 MB bot upload limit and will be skipped.");
        }

        // Estimate: ~2 messages/sec, ~1 reaction/sec
        var estimatedSeconds = messageCount / 2.0;
        if (options.IncludeReactions)
        {
            estimatedSeconds += reactionCount;
        }

        var estimatedDuration = TimeSpan.FromSeconds(estimatedSeconds);

        _logger.Information(
            "Preview complete: {MessageCount} messages, {AttachmentCount} attachments, {OversizedCount} oversized, estimated {Duration}",
            messageCount, attachmentCount, oversizedAttachments.Count, estimatedDuration);

        return new MigrationPreview(
            messageCount,
            attachmentCount,
            totalAttachmentBytes,
            oversizedAttachments,
            estimatedDuration,
            warnings);
    }

    /// <summary>
    /// Runs a full migration from the source channel to the destination channel.
    /// </summary>
    /// <param name="options">The migration options.</param>
    /// <param name="progress">Progress reporter for UI updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="MigrationResult"/> describing the outcome of the migration.</returns>
    public async Task<MigrationResult> RunAsync(
        MigrationOptions options,
        IProgress<MigrationProgress> progress,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.Information("Starting migration from {Source} to {Destination}",
            options.SourceChannelId, options.DestinationChannelId);

        // Create webhook in the destination channel
        Webhook webhook;
        if (options.DryRun)
        {
            _logger.Information("Dry run mode: skipping webhook creation");
            webhook = new Webhook { Id = "dry-run", Token = "dry-run" };
        }
        else
        {
            webhook = await _webhookEndpoints.CreateWebhookAsync(
                options.DestinationChannelId, "Discord Conduit Migration", ct);

            if (string.IsNullOrEmpty(webhook.Token))
            {
                throw new InvalidOperationException(
                    $"Discord returned a webhook without a token (ID: {webhook.Id}). " +
                    "The bot may lack MANAGE_WEBHOOKS permission.");
            }

            _logger.Information("Created webhook {WebhookId} in channel {ChannelId}",
                webhook.Id, options.DestinationChannelId);
        }

        // Initialize state
        var migrationId = MigrationState.GenerateMigrationId(options.SourceChannelId, options.DestinationChannelId);
        var state = new MigrationState
        {
            MigrationId = migrationId,
            SourceChannelId = options.SourceChannelId,
            DestinationChannelId = options.DestinationChannelId,
            GuildId = options.GuildId,
            WebhookId = webhook.Id,
            WebhookToken = webhook.Token ?? string.Empty,
            StartedAt = DateTimeOffset.UtcNow,
            Phase = MigrationPhase.MigratingMessages,
            Options = options
        };

        // Fetch all messages and process in chronological order
        var allMessages = await FetchAllMessagesChronologicalAsync(options.SourceChannelId, ct);
        state.TotalMessageCount = allMessages.Count;

        await state.SaveAsync(_appDataPath);

        var result = await MigrateMessagesAsync(state, allMessages, webhook, stopwatch, progress, ct);

        // Clean up webhook if not a dry run
        if (!options.DryRun)
        {
            try
            {
                await _webhookEndpoints.DeleteWebhookAsync(webhook.Id, ct);
                _logger.Information("Deleted migration webhook {WebhookId}", webhook.Id);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete migration webhook {WebhookId}", webhook.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Resumes a previously interrupted migration from saved state.
    /// </summary>
    /// <param name="state">The saved migration state to resume from.</param>
    /// <param name="progress">Progress reporter for UI updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="MigrationResult"/> describing the outcome of the migration.</returns>
    public async Task<MigrationResult> ResumeAsync(
        MigrationState state,
        IProgress<MigrationProgress> progress,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.Information("Resuming migration {MigrationId} from message {LastMessageId}",
            state.MigrationId, state.LastSuccessfulSourceMessageId);

        // Verify the webhook still exists; if not, create a new one
        var webhook = await EnsureWebhookAsync(state, ct);

        // Fetch remaining messages after the last successful one
        List<Message> remainingMessages;
        if (state.LastSuccessfulSourceMessageId is not null)
        {
            remainingMessages = await FetchMessagesAfterAsync(
                state.SourceChannelId, state.LastSuccessfulSourceMessageId, ct);
        }
        else
        {
            remainingMessages = await FetchAllMessagesChronologicalAsync(state.SourceChannelId, ct);
        }

        state.TotalMessageCount = state.MigratedCount + remainingMessages.Count;
        state.Phase = MigrationPhase.MigratingMessages;

        // For reactions, we need ALL source messages (including already-migrated ones),
        // not just the remaining slice, so reactions for previously migrated messages aren't skipped
        List<Message>? allMessagesForReactions = null;
        if (state.Options.IncludeReactions && !state.Options.DryRun)
        {
            allMessagesForReactions = await FetchAllMessagesChronologicalAsync(state.SourceChannelId, ct);
        }

        var result = await MigrateMessagesAsync(state, remainingMessages, webhook, stopwatch, progress, ct, allMessagesForReactions);

        // Clean up webhook if not a dry run
        if (!state.Options.DryRun)
        {
            try
            {
                await _webhookEndpoints.DeleteWebhookAsync(webhook.Id, ct);
                _logger.Information("Deleted migration webhook {WebhookId}", webhook.Id);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete migration webhook {WebhookId}", webhook.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Migrates a list of messages through the webhook, handling attachments, reactions, and state persistence.
    /// </summary>
    private async Task<MigrationResult> MigrateMessagesAsync(
        MigrationState state,
        List<Message> messages,
        Webhook webhook,
        Stopwatch stopwatch,
        IProgress<MigrationProgress> progress,
        CancellationToken ct,
        List<Message>? allMessagesForReactions = null)
    {
        var skipped = 0;

        foreach (var message in messages)
        {
            ct.ThrowIfCancellationRequested();

            // Skip if already migrated (resume case)
            if (state.MessageIdMap.ContainsKey(message.Id))
            {
                continue;
            }

            // Skip system messages (only type 0 = default and 19 = reply are regular)
            if (!message.IsRegularMessage)
            {
                skipped++;
                _logger.Debug("Skipping system message {MessageId} (type {Type})", message.Id, message.Type);
                ReportProgress(state, skipped, stopwatch.Elapsed, progress);
                continue;
            }

            try
            {
                await MigrateSingleMessageAsync(state, message, webhook, ct);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to migrate message {MessageId}", message.Id);
                state.FailedMessages.Add(new FailedMessage(
                    message.Id,
                    ex.Message,
                    DateTimeOffset.UtcNow));
            }

            ReportProgress(state, skipped, stopwatch.Elapsed, progress);
        }

        // Reaction migration phase — use full message list if provided (resume case),
        // otherwise use the messages we just processed (fresh migration case)
        if (state.Options.IncludeReactions && !state.Options.DryRun)
        {
            state.Phase = MigrationPhase.MigratingReactions;
            await state.SaveAsync(_appDataPath);
            ReportProgress(state, skipped, stopwatch.Elapsed, progress);

            await MigrateReactionsAsync(state, allMessagesForReactions ?? messages, ct);
        }

        // Finalize
        state.Phase = MigrationPhase.Complete;
        await state.SaveAsync(_appDataPath);

        stopwatch.Stop();

        var result = new MigrationResult(
            state.MigratedCount,
            state.FailedMessages.Count,
            skipped,
            stopwatch.Elapsed,
            state.FailedMessages);

        ReportProgress(state, skipped, stopwatch.Elapsed, progress);

        _logger.Information(
            "Migration complete: {Migrated} migrated, {Failed} failed, {Skipped} skipped in {Duration}",
            result.TotalMigrated, result.TotalFailed, result.TotalSkipped, result.Duration);

        return result;
    }

    /// <summary>
    /// Migrates a single message through the webhook, including attachment handling.
    /// </summary>
    private async Task MigrateSingleMessageAsync(
        MigrationState state,
        Message message,
        Webhook webhook,
        CancellationToken ct)
    {
        var replyReference = _messageMigrator.BuildReplyReference(message, state.MessageIdMap);
        var content = _messageMigrator.BuildWebhookContent(message, replyReference);
        var username = _messageMigrator.GetWebhookUsername(message);
        var avatarUrl = _messageMigrator.GetWebhookAvatarUrl(message);

        // Collect non-oversized attachments
        var uploadableAttachments = new List<Attachment>();
        if (message.Attachments is { Count: > 0 })
        {
            foreach (var attachment in message.Attachments)
            {
                if (!_attachmentHandler.IsOversized(attachment))
                {
                    uploadableAttachments.Add(attachment);
                }
                else
                {
                    _logger.Warning("Skipping oversized attachment {Filename} ({Size} bytes) on message {MessageId}",
                        attachment.Filename, attachment.Size, message.Id);
                }
            }
        }

        // Collect bot embeds (type "rich") -- link embeds have type "link", "image", "video", etc.
        List<Embed>? richEmbeds = null;
        if (message.Author.Bot == true && message.Embeds is { Count: > 0 })
        {
            richEmbeds = message.Embeds
                .Where(e => e.Type is "rich")
                .ToList();

            if (richEmbeds.Count == 0)
            {
                richEmbeds = null;
            }
        }

        if (state.Options.DryRun)
        {
            _logger.Information("[DRY RUN] Would migrate message {MessageId} by {Author}: {Preview}",
                message.Id, username, Truncate(content, 80));
            state.MessageIdMap[message.Id] = "dry-run";
            state.MigratedCount++;
            state.LastSuccessfulSourceMessageId = message.Id;
            return;
        }

        Message repostedMessage;

        if (uploadableAttachments.Count > 0)
        {
            // Download attachments and post via multipart
            var files = new List<(byte[] Data, string Filename, string? ContentType)>();
            foreach (var attachment in uploadableAttachments)
            {
                var downloaded = await _attachmentHandler.DownloadAttachmentAsync(attachment, ct);
                files.Add(downloaded);
            }

            repostedMessage = await _webhookEndpoints.ExecuteWebhookWithFilesAsync(
                webhook.Id, webhook.Token!,
                () => _attachmentHandler.CreateMultipartContent(content, username, avatarUrl, richEmbeds, files),
                ct);
        }
        else
        {
            // Post via JSON payload
            var payload = new WebhookExecutePayload
            {
                Content = content,
                Username = username,
                AvatarUrl = avatarUrl,
                Embeds = richEmbeds
            };

            repostedMessage = await _webhookEndpoints.ExecuteWebhookAsync(
                webhook.Id, webhook.Token!, payload, ct);
        }

        // Update state
        state.MessageIdMap[message.Id] = repostedMessage.Id;
        state.MigratedCount++;
        state.LastSuccessfulSourceMessageId = message.Id;

        // Save state after every successful message for maximum resume safety
        await state.SaveAsync(_appDataPath);

        _logger.Debug("Migrated message {SourceId} -> {DestId}",
            message.Id, repostedMessage.Id);
    }

    /// <summary>
    /// Migrates reactions from source messages to their corresponding destination messages.
    /// </summary>
    private async Task MigrateReactionsAsync(
        MigrationState state,
        List<Message> sourceMessages,
        CancellationToken ct)
    {
        _logger.Information("Starting reaction migration phase");

        foreach (var message in sourceMessages)
        {
            ct.ThrowIfCancellationRequested();

            if (message.Reactions is not { Count: > 0 })
                continue;

            if (!state.MessageIdMap.TryGetValue(message.Id, out var repostedMessageId))
                continue;

            foreach (var reaction in message.Reactions)
            {
                try
                {
                    await _reactionEndpoints.CreateReactionAsync(
                        state.DestinationChannelId,
                        repostedMessageId,
                        reaction.Emoji.ApiIdentifier,
                        ct);

                    _logger.Debug("Added reaction {Emoji} to message {MessageId}",
                        reaction.Emoji.ApiIdentifier, repostedMessageId);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to add reaction {Emoji} to message {MessageId}",
                        reaction.Emoji.ApiIdentifier, repostedMessageId);
                }
            }
        }

        _logger.Information("Reaction migration phase complete");
    }

    /// <summary>
    /// Fetches all messages from a channel in descending order (newest first) using before= pagination.
    /// </summary>
    private async Task<List<Message>> FetchAllMessagesDescendingAsync(string channelId, CancellationToken ct)
    {
        var allMessages = new List<Message>();
        string? beforeId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await _messageEndpoints.GetMessagesAsync(channelId, 100, before: beforeId, ct: ct);

            if (batch.Count == 0)
                break;

            allMessages.AddRange(batch);
            beforeId = batch[^1].Id;

            _logger.Debug("Fetched {Count} messages (total so far: {Total})", batch.Count, allMessages.Count);

            if (batch.Count < 100)
                break;
        }

        return allMessages;
    }

    /// <summary>
    /// Fetches all messages from a channel in chronological order (oldest first).
    /// Uses before= pagination to fetch all messages, then reverses the list.
    /// </summary>
    private async Task<List<Message>> FetchAllMessagesChronologicalAsync(string channelId, CancellationToken ct)
    {
        var allMessages = await FetchAllMessagesDescendingAsync(channelId, ct);
        allMessages.Reverse();
        return allMessages;
    }

    /// <summary>
    /// Fetches all messages after a given message ID in chronological order using after= pagination.
    /// </summary>
    private async Task<List<Message>> FetchMessagesAfterAsync(
        string channelId, string afterMessageId, CancellationToken ct)
    {
        var allMessages = new List<Message>();
        var afterId = afterMessageId;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await _messageEndpoints.GetMessagesAsync(channelId, 100, after: afterId, ct: ct);

            if (batch.Count == 0)
                break;

            // Discord returns messages in descending order even with after=, so reverse each batch
            batch.Reverse();
            allMessages.AddRange(batch);
            afterId = allMessages[^1].Id;

            _logger.Debug("Fetched {Count} messages after {AfterId} (total so far: {Total})",
                batch.Count, afterMessageId, allMessages.Count);

            if (batch.Count < 100)
                break;
        }

        return allMessages;
    }

    /// <summary>
    /// Ensures the webhook from saved state still exists. Creates a new one if it does not.
    /// </summary>
    private async Task<Webhook> EnsureWebhookAsync(MigrationState state, CancellationToken ct)
    {
        if (state.Options.DryRun)
        {
            return new Webhook { Id = "dry-run", Token = "dry-run" };
        }

        try
        {
            // Fetch the webhook by ID to get its token (token is not persisted to disk for security)
            var webhook = await _webhookEndpoints.GetWebhookAsync(state.WebhookId, ct);
            state.WebhookToken = webhook.Token ?? string.Empty;
            _logger.Debug("Verified existing webhook {WebhookId}", webhook.Id);
            return webhook;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Webhook {WebhookId} no longer exists, creating a new one", state.WebhookId);

            var webhook = await _webhookEndpoints.CreateWebhookAsync(
                state.DestinationChannelId, "Discord Conduit Migration", ct);

            state.WebhookId = webhook.Id;
            state.WebhookToken = webhook.Token ?? string.Empty;
            await state.SaveAsync(_appDataPath);

            _logger.Information("Created new webhook {WebhookId} for resumed migration", webhook.Id);
            return webhook;
        }
    }

    /// <summary>
    /// Reports the current migration progress.
    /// </summary>
    private static void ReportProgress(
        MigrationState state,
        int skipped,
        TimeSpan elapsed,
        IProgress<MigrationProgress> progress)
    {
        var completed = state.MigratedCount;
        var total = state.TotalMessageCount;
        var failed = state.FailedMessages.Count;

        TimeSpan? estimatedRemaining = null;
        if (completed > 0 && total > 0)
        {
            var rate = elapsed.TotalSeconds / completed;
            var remaining = (total - completed - skipped - failed) * rate;
            if (remaining > 0)
            {
                estimatedRemaining = TimeSpan.FromSeconds(remaining);
            }
        }

        progress.Report(new MigrationProgress(
            completed,
            total,
            failed,
            skipped,
            CurrentMessagePreview: null,
            elapsed,
            estimatedRemaining,
            state.Phase));
    }

    /// <summary>
    /// Truncates a string to the specified maximum length, appending an ellipsis if truncated.
    /// </summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
            return value;

        return value[..maxLength] + "...";
    }
}
