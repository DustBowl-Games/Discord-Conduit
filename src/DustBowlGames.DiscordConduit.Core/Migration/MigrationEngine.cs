using System.Diagnostics;
using System.Text.Json;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Models;
using DustBowlGames.DiscordConduit.Core.Json;
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

        var allMessages = await FetchAllMessagesDescendingAsync(options.SourceChannelId, ct).ConfigureAwait(false);
        allMessages = ApplyFilter(allMessages, options.Filter, _logger);

        var messageCount = allMessages.Count;
        var attachmentCount = 0;
        long totalAttachmentBytes = 0;
        var oversizedAttachments = new List<OversizedAttachment>();
        var warnings = new List<string>();
        var reactionCount = 0;
        var stickerMessageCount = 0;

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

            if (message.HasStickers)
            {
                stickerMessageCount++;
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
            var limitMb = Attachment.MaxBotUploadSize / (1024 * 1024);
            warnings.Add($"{oversizedAttachments.Count} attachment(s) exceed the {limitMb} MB bot upload limit and will be skipped.");
        }

        if (stickerMessageCount > 0)
        {
            warnings.Add($"{stickerMessageCount} message(s) contain stickers — PNG/APNG/GIF stickers are re-posted as images; animated (Lottie) stickers cannot be migrated.");
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
    /// <param name="pauseToken">
    /// An optional pause token. When its source is paused, the migration suspends at the start of
    /// each per-message iteration and resumes when the source is resumed. A default token never pauses.
    /// </param>
    /// <returns>A <see cref="MigrationResult"/> describing the outcome of the migration.</returns>
    public async Task<MigrationResult> RunAsync(
        MigrationOptions options,
        IProgress<MigrationProgress> progress,
        CancellationToken ct,
        PauseToken pauseToken = default)
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
                options.DestinationChannelId, "Conduit Migration", ct).ConfigureAwait(false);

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

        // Create per-migration log file
        if (!System.Text.RegularExpressions.Regex.IsMatch(state.MigrationId, @"^[A-Za-z0-9_-]+$"))
            throw new ArgumentException($"Invalid migration ID format: {state.MigrationId}");
        var migrationLogPath = Path.Combine(_appDataPath, "migrations", state.MigrationId, "migration.log");
        Directory.CreateDirectory(Path.GetDirectoryName(migrationLogPath)!);
        // Use {Message:j} to JSON-escape log message values, preventing log injection
        // from Discord-sourced content (usernames, message text)
        var migrationLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(migrationLogPath,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:j}{NewLine}{Exception}")
            .CreateLogger();

        migrationLogger.Information("Migration started: {MigrationId} from {Source} to {Destination} in guild {Guild}",
            state.MigrationId, options.SourceChannelId, options.DestinationChannelId, options.GuildId);
        migrationLogger.Information("Options: DryRun={DryRun}, IncludeReactions={IncludeReactions}",
            options.DryRun, options.IncludeReactions);

        string status = "completed";
        try
        {
            // Fetch all messages and process in chronological order
            var allMessages = await FetchAllMessagesChronologicalAsync(options.SourceChannelId, ct).ConfigureAwait(false);
            allMessages = ApplyFilter(allMessages, options.Filter, migrationLogger);
            state.TotalMessageCount = allMessages.Count;

            await state.SaveAsync(_appDataPath).ConfigureAwait(false);

            var result = await MigrateMessagesAsync(state, allMessages, webhook, stopwatch, progress, migrationLogger, ct, pauseToken: pauseToken).ConfigureAwait(false);

            // Clean up webhook if not a dry run
            if (!options.DryRun)
            {
                try
                {
                    await _webhookEndpoints.DeleteWebhookAsync(webhook.Id, ct).ConfigureAwait(false);
                    _logger.Information("Deleted migration webhook {WebhookId}", webhook.Id);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to delete migration webhook {WebhookId}", webhook.Id);
                }
            }

            migrationLogger.Information(
                "Migration completed: {Migrated} migrated, {Failed} failed, {Skipped} skipped in {Duration}",
                result.TotalMigrated, result.TotalFailed, result.TotalSkipped, result.Duration);

            // Write report.json
            await WriteReportAsync(state, result, status).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            status = "cancelled";
            migrationLogger.Warning("Migration cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            status = "failed";
            migrationLogger.Error(ex, "Migration failed");
            throw;
        }
        finally
        {
            migrationLogger.Dispose();
        }
    }

    /// <summary>
    /// Resumes a previously interrupted migration from saved state.
    /// </summary>
    /// <param name="state">The saved migration state to resume from.</param>
    /// <param name="progress">Progress reporter for UI updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="pauseToken">
    /// An optional pause token. When its source is paused, the migration suspends at the start of
    /// each per-message iteration and resumes when the source is resumed. A default token never pauses.
    /// </param>
    /// <returns>A <see cref="MigrationResult"/> describing the outcome of the migration.</returns>
    public async Task<MigrationResult> ResumeAsync(
        MigrationState state,
        IProgress<MigrationProgress> progress,
        CancellationToken ct,
        PauseToken pauseToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.Information("Resuming migration {MigrationId} from message {LastMessageId}",
            state.MigrationId, state.LastSuccessfulSourceMessageId);

        // Resume state is deserialized from an arbitrary on-disk JSON file, so every snowflake-typed
        // field is attacker-controllable. Validate each one before it can flow into a Discord REST
        // URL path, where characters like '/', '?' or '..' would otherwise redirect the bot's
        // authenticated request to a different API route (path-injection / confused-deputy).
        RequireSnowflake(state.SourceChannelId, nameof(state.SourceChannelId));
        RequireSnowflake(state.DestinationChannelId, nameof(state.DestinationChannelId));
        RequireSnowflake(state.GuildId, nameof(state.GuildId));
        RequireSnowflake(state.WebhookId, nameof(state.WebhookId));
        if (state.LastSuccessfulSourceMessageId is not null)
            RequireSnowflake(state.LastSuccessfulSourceMessageId, nameof(state.LastSuccessfulSourceMessageId));
        foreach (var entry in state.MessageIdMap)
        {
            RequireSnowflake(entry.Key, "MessageIdMap key");
            // "dry-run" is a legitimate placeholder value written for dry-run map entries.
            if (entry.Value != "dry-run")
                RequireSnowflake(entry.Value, "MessageIdMap value");
        }

        // Create per-migration log file (append to existing if resuming)
        if (!System.Text.RegularExpressions.Regex.IsMatch(state.MigrationId, @"^[A-Za-z0-9_-]+$"))
            throw new ArgumentException($"Invalid migration ID format: {state.MigrationId}");
        var migrationLogPath = Path.Combine(_appDataPath, "migrations", state.MigrationId, "migration.log");
        Directory.CreateDirectory(Path.GetDirectoryName(migrationLogPath)!);
        var migrationLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(migrationLogPath,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:j}{NewLine}{Exception}")
            .CreateLogger();

        migrationLogger.Information("Migration resumed: {MigrationId} from message {LastMessageId}",
            state.MigrationId, state.LastSuccessfulSourceMessageId);

        string status = "completed";
        try
        {
            // Verify the webhook still exists; if not, create a new one
            var webhook = await EnsureWebhookAsync(state, ct).ConfigureAwait(false);

            // Fetch remaining messages after the last successful one
            List<Message> remainingMessages;
            if (state.LastSuccessfulSourceMessageId is not null)
            {
                remainingMessages = await FetchMessagesAfterAsync(
                    state.SourceChannelId, state.LastSuccessfulSourceMessageId, ct).ConfigureAwait(false);
            }
            else
            {
                remainingMessages = await FetchAllMessagesChronologicalAsync(state.SourceChannelId, ct).ConfigureAwait(false);
            }

            remainingMessages = ApplyFilter(remainingMessages, state.Options.Filter, migrationLogger);

            state.TotalMessageCount = state.MigratedCount + remainingMessages.Count;
            state.Phase = MigrationPhase.MigratingMessages;

            // For reactions, we need ALL source messages (including already-migrated ones),
            // not just the remaining slice, so reactions for previously migrated messages aren't skipped
            List<Message>? allMessagesForReactions = null;
            if (state.Options.IncludeReactions && !state.Options.DryRun)
            {
                allMessagesForReactions = await FetchAllMessagesChronologicalAsync(state.SourceChannelId, ct).ConfigureAwait(false);
            }

            var result = await MigrateMessagesAsync(state, remainingMessages, webhook, stopwatch, progress, migrationLogger, ct, allMessagesForReactions, pauseToken).ConfigureAwait(false);

            // Clean up webhook if not a dry run
            if (!state.Options.DryRun)
            {
                try
                {
                    await _webhookEndpoints.DeleteWebhookAsync(webhook.Id, ct).ConfigureAwait(false);
                    _logger.Information("Deleted migration webhook {WebhookId}", webhook.Id);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to delete migration webhook {WebhookId}", webhook.Id);
                }
            }

            migrationLogger.Information(
                "Migration completed: {Migrated} migrated, {Failed} failed, {Skipped} skipped in {Duration}",
                result.TotalMigrated, result.TotalFailed, result.TotalSkipped, result.Duration);

            // Write report.json
            await WriteReportAsync(state, result, status).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            status = "cancelled";
            migrationLogger.Warning("Migration cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            status = "failed";
            migrationLogger.Error(ex, "Migration failed");
            throw;
        }
        finally
        {
            migrationLogger.Dispose();
        }
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
        ILogger migrationLogger,
        CancellationToken ct,
        List<Message>? allMessagesForReactions = null,
        PauseToken pauseToken = default)
    {
        var skipped = 0;

        foreach (var message in messages)
        {
            ct.ThrowIfCancellationRequested();
            await pauseToken.WaitWhilePausedAsync(ct).ConfigureAwait(false);

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
                await MigrateSingleMessageAsync(state, message, webhook, migrationLogger, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // User cancellation must stop the migration, not be recorded as a failed message.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to migrate message {MessageId}", message.Id);
                migrationLogger.Error(ex, "Failed to migrate message {MessageId}: {Reason}", message.Id, ex.Message);
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
            await state.SaveAsync(_appDataPath).ConfigureAwait(false);
            ReportProgress(state, skipped, stopwatch.Elapsed, progress);

            await MigrateReactionsAsync(state, allMessagesForReactions ?? messages, ct).ConfigureAwait(false);
        }

        // Pin migration phase — re-pin messages that were pinned in the source channel.
        if (state.Options.IncludePins && !state.Options.DryRun)
        {
            await MigratePinsAsync(state, allMessagesForReactions ?? messages, ct).ConfigureAwait(false);
        }

        // Finalize
        state.Phase = MigrationPhase.Complete;
        await state.SaveAsync(_appDataPath).ConfigureAwait(false);

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
        ILogger migrationLogger,
        CancellationToken ct)
    {
        var replyReference = _messageMigrator.BuildReplyReference(message, state.MessageIdMap);
        var content = _messageMigrator.BuildWebhookContent(message, replyReference,
            includeStickers: state.Options.IncludeStickers, includeTimestamp: state.Options.IncludeTimestamps);
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

        // Defensively sanitize forwarded bot embeds (cap count, strip non-http URLs).
        richEmbeds = EmbedSanitizer.Sanitize(richEmbeds);

        // Re-create an attached poll if present and enabled (votes/original timing can't be carried over).
        var poll = state.Options.IncludePolls ? message.Poll?.ToCreateRequest() : null;

        if (state.Options.DryRun)
        {
            _logger.Information("[DRY RUN] Would migrate message {MessageId} by {Author}: {Preview}",
                message.Id, username, Truncate(content, 80));
            state.MessageIdMap[message.Id] = "dry-run";
            state.MigratedCount++;
            state.LastSuccessfulSourceMessageId = message.Id;
            return;
        }

        // A webhook payload with no text, no uploadable files, and no rich embeds is rejected by
        // Discord with a 400. Skip such messages rather than failing the whole send.
        if (string.IsNullOrWhiteSpace(content)
            && uploadableAttachments.Count == 0
            && richEmbeds is not { Count: > 0 }
            && poll is null)
        {
            _logger.Warning(
                "Skipping message {MessageId}: no migratable content (empty text, no uploadable attachments, no rich embeds)",
                message.Id);
            migrationLogger.Warning(
                "Skipping message {MessageId}: no migratable content (empty text, no uploadable attachments, no rich embeds)",
                message.Id);
            state.FailedMessages.Add(new FailedMessage(
                message.Id,
                "No migratable content (empty text, no uploadable attachments, no rich embeds).",
                DateTimeOffset.UtcNow));
            return;
        }

        Message repostedMessage;

        if (uploadableAttachments.Count > 0)
        {
            // Download attachments and post via multipart
            var files = new List<(byte[] Data, string Filename, string? ContentType)>();
            foreach (var attachment in uploadableAttachments)
            {
                var downloaded = await _attachmentHandler.DownloadAttachmentAsync(attachment, ct).ConfigureAwait(false);
                files.Add(downloaded);
            }

            repostedMessage = await _webhookEndpoints.ExecuteWebhookWithFilesAsync(
                webhook.Id, webhook.Token!,
                () => _attachmentHandler.CreateMultipartContent(content, username, avatarUrl, richEmbeds, files, poll),
                ct).ConfigureAwait(false);
        }
        else
        {
            // Post via JSON payload
            var payload = new WebhookExecutePayload
            {
                Content = content,
                Username = username,
                AvatarUrl = avatarUrl,
                Embeds = richEmbeds,
                Poll = poll
            };

            repostedMessage = await _webhookEndpoints.ExecuteWebhookAsync(
                webhook.Id, webhook.Token!, payload, ct).ConfigureAwait(false);
        }

        // Update state
        state.MessageIdMap[message.Id] = repostedMessage.Id;
        state.MigratedCount++;
        state.LastSuccessfulSourceMessageId = message.Id;

        // Save state after every successful message for maximum resume safety
        await state.SaveAsync(_appDataPath).ConfigureAwait(false);

        _logger.Debug("Migrated message {SourceId} -> {DestId}",
            message.Id, repostedMessage.Id);
        migrationLogger.Debug("Migrated message {SourceId} -> {DestId}",
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
                        ct).ConfigureAwait(false);

                    _logger.Debug("Added reaction {Emoji} to message {MessageId}",
                        reaction.Emoji.ApiIdentifier, repostedMessageId);
                }
                catch (OperationCanceledException)
                {
                    // User cancellation must stop the reaction pass promptly, not be swallowed.
                    throw;
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
    /// Applies the optional message filter, returning only matching messages and logging how many
    /// were excluded so the effect is visible.
    /// </summary>
    private static List<Message> ApplyFilter(List<Message> messages, MessageFilter? filter, ILogger logger)
    {
        if (filter is null || !filter.IsActive)
            return messages;

        var filtered = messages.Where(filter.Matches).ToList();
        logger.Information("Message filter applied: {Kept} of {Total} messages match the criteria",
            filtered.Count, messages.Count);
        return filtered;
    }

    /// <summary>
    /// Re-pins migrated messages whose source message was pinned, after posting so the destination
    /// message IDs are known. Best-effort: per-pin failures (e.g. Discord's 50-pin channel limit)
    /// are logged and skipped. Messages are pinned in chronological order so pin ordering roughly
    /// matches the source.
    /// </summary>
    private async Task MigratePinsAsync(MigrationState state, List<Message> sourceMessages, CancellationToken ct)
    {
        _logger.Information("Starting pin migration phase");

        foreach (var message in sourceMessages)
        {
            ct.ThrowIfCancellationRequested();

            if (!message.Pinned)
                continue;

            if (!state.MessageIdMap.TryGetValue(message.Id, out var repostedMessageId))
                continue;

            try
            {
                await _messageEndpoints.PinMessageAsync(state.DestinationChannelId, repostedMessageId, ct).ConfigureAwait(false);
                _logger.Debug("Pinned migrated message {MessageId}", repostedMessageId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to pin migrated message {MessageId}", repostedMessageId);
            }
        }

        _logger.Information("Pin migration phase complete");
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

            var batch = await _messageEndpoints.GetMessagesAsync(channelId, 100, before: beforeId, ct: ct).ConfigureAwait(false);

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
        var allMessages = await FetchAllMessagesDescendingAsync(channelId, ct).ConfigureAwait(false);
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

            var batch = await _messageEndpoints.GetMessagesAsync(channelId, 100, after: afterId, ct: ct).ConfigureAwait(false);

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
            var webhook = await _webhookEndpoints.GetWebhookAsync(state.WebhookId, ct).ConfigureAwait(false);
            state.WebhookToken = webhook.Token ?? string.Empty;
            _logger.Debug("Verified existing webhook {WebhookId}", webhook.Id);
            return webhook;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Webhook {WebhookId} no longer exists, creating a new one", state.WebhookId);

            var webhook = await _webhookEndpoints.CreateWebhookAsync(
                state.DestinationChannelId, "Conduit Migration", ct).ConfigureAwait(false);

            state.WebhookId = webhook.Id;
            state.WebhookToken = webhook.Token ?? string.Empty;
            await state.SaveAsync(_appDataPath).ConfigureAwait(false);

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
    /// Writes a JSON report summarizing the migration outcome.
    /// </summary>
    private async Task WriteReportAsync(MigrationState state, MigrationResult result, string status)
    {
        try
        {
            var report = new
            {
                migrationId = state.MigrationId,
                startedAt = state.StartedAt,
                completedAt = DateTimeOffset.UtcNow,
                status,
                sourceChannelId = state.SourceChannelId,
                destinationChannelId = state.DestinationChannelId,
                guildId = state.GuildId,
                options = new
                {
                    dryRun = state.Options.DryRun,
                    includeReactions = state.Options.IncludeReactions
                },
                results = new
                {
                    totalMigrated = result.TotalMigrated,
                    totalFailed = result.TotalFailed,
                    totalSkipped = result.TotalSkipped,
                    duration = result.Duration.ToString()
                },
                failedMessages = result.FailedMessages.Select(f => new
                {
                    sourceMessageId = f.SourceMessageId,
                    reason = f.Reason,
                    timestamp = f.Timestamp
                })
            };

            if (!System.Text.RegularExpressions.Regex.IsMatch(state.MigrationId, @"^[A-Za-z0-9_-]+$"))
                throw new ArgumentException($"Invalid migration ID format: {state.MigrationId}");
            var reportPath = Path.Combine(_appDataPath, "migrations", state.MigrationId, "report.json");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, CoreJsonOptions.Default)).ConfigureAwait(false);
            _logger.Debug("Wrote migration report to {ReportPath}", reportPath);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to write migration report for {MigrationId}", state.MigrationId);
        }
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if <paramref name="value"/> is not a valid Discord
    /// snowflake. Used to validate attacker-controllable fields loaded from a resume-state file
    /// before they are interpolated into REST URL paths.
    /// </summary>
    private static void RequireSnowflake(string? value, string fieldName)
    {
        if (!Snowflake.IsValid(value))
            throw new ArgumentException($"Resume state contains an invalid Discord ID for {fieldName}.", nameof(value));
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

/// <summary>
/// Defensive sanitization for bot-authored rich embeds that are forwarded verbatim through a
/// webhook. Shared by <see cref="MigrationEngine"/> and the move-command handler so both apply
/// the same hardening: Discord allows at most 10 embeds per message, and any URL field whose
/// scheme is not http/https is nulled out so non-http URL schemes cannot be injected.
/// </summary>
internal static class EmbedSanitizer
{
    /// <summary>Discord's hard limit on embeds per message.</summary>
    private const int MaxEmbeds = 10;

    /// <summary>
    /// Returns a sanitized copy of <paramref name="embeds"/> with the count capped at 10 and every
    /// non-http(s) URL field removed, or <c>null</c> if the input is null or empty.
    /// </summary>
    /// <param name="embeds">The embeds to sanitize, or <c>null</c>.</param>
    /// <returns>A sanitized list, or <c>null</c> when there is nothing to send.</returns>
    public static List<Embed>? Sanitize(List<Embed>? embeds)
    {
        if (embeds is not { Count: > 0 })
            return null;

        return embeds.Take(MaxEmbeds).Select(SanitizeEmbed).ToList();
    }

    private static Embed SanitizeEmbed(Embed embed)
    {
        // Embed and its sub-objects are init-only, so rebuild rather than mutate.
        return new Embed
        {
            Title = embed.Title,
            Type = embed.Type,
            Description = embed.Description,
            Url = SafeUrl(embed.Url),
            Timestamp = embed.Timestamp,
            Color = embed.Color,
            Footer = embed.Footer is null
                ? null
                : new EmbedFooter { Text = embed.Footer.Text, IconUrl = SafeUrl(embed.Footer.IconUrl) },
            Image = SanitizeMedia(embed.Image),
            Thumbnail = SanitizeMedia(embed.Thumbnail),
            Author = embed.Author is null
                ? null
                : new EmbedAuthor
                {
                    Name = embed.Author.Name,
                    Url = SafeUrl(embed.Author.Url),
                    IconUrl = SafeUrl(embed.Author.IconUrl)
                },
            Fields = embed.Fields
        };
    }

    private static EmbedMedia? SanitizeMedia(EmbedMedia? media)
    {
        if (media is null)
            return null;

        // EmbedMedia.Url is required (non-null). If the source URL is not http(s), drop the whole
        // media object rather than emit one with a blank required URL.
        return IsHttpUrl(media.Url)
            ? media
            : null;
    }

    private static string? SafeUrl(string? url) => IsHttpUrl(url) ? url : null;

    private static bool IsHttpUrl(string? url) =>
        url is not null &&
        (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
