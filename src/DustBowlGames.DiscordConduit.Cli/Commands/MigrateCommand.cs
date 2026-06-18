using System.CommandLine;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Credentials;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Profiles;
using Serilog;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class MigrateCommand
{
    public static Command Create(string appDataPath)
    {
        var command = new Command("migrate") { Description = "Run a message migration" };

        var profileOption = new Option<string>("--profile") { Description = "Bot profile name", Required = true };
        var sourceOption = new Option<string>("--source") { Description = "Source channel ID", Required = true };
        var destOption = new Option<string>("--dest") { Description = "Destination channel ID", Required = true };
        var guildOption = new Option<string>("--guild") { Description = "Guild (server) ID", Required = true };
        var dryRunOption = new Option<bool>("--dry-run") { Description = "Validate without posting" };
        var noReactionsOption = new Option<bool>("--no-reactions") { Description = "Skip reaction migration" };
        var noPinsOption = new Option<bool>("--no-pins") { Description = "Don't re-pin messages that were pinned in the source" };
        var noPollsOption = new Option<bool>("--no-polls") { Description = "Don't re-create polls attached to messages" };
        var noStickersOption = new Option<bool>("--no-stickers") { Description = "Don't post sticker images when a message has stickers" };
        var timestampsOption = new Option<bool>("--timestamps") { Description = "Append each message's original send time as a footer" };
        var fromAuthorOption = new Option<string?>("--from-author") { Description = "Only migrate messages from this author (user ID)" };
        var sinceOption = new Option<string?>("--since") { Description = "Only migrate messages on/after this date/time (e.g. 2024-01-01 or 2024-01-01T12:00:00Z)" };
        var untilOption = new Option<string?>("--until") { Description = "Only migrate messages on/before this date/time" };
        var containsOption = new Option<string?>("--contains") { Description = "Only migrate messages whose text contains this (case-insensitive)" };
        var attachmentsOnlyOption = new Option<bool>("--attachments-only") { Description = "Only migrate messages that have attachments" };
        var noBotsOption = new Option<bool>("--no-bots") { Description = "Exclude messages authored by bots" };

        command.Options.Add(profileOption);
        command.Options.Add(sourceOption);
        command.Options.Add(destOption);
        command.Options.Add(guildOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(noReactionsOption);
        command.Options.Add(noPinsOption);
        command.Options.Add(noPollsOption);
        command.Options.Add(noStickersOption);
        command.Options.Add(timestampsOption);
        command.Options.Add(fromAuthorOption);
        command.Options.Add(sinceOption);
        command.Options.Add(untilOption);
        command.Options.Add(containsOption);
        command.Options.Add(attachmentsOnlyOption);
        command.Options.Add(noBotsOption);

        command.SetAction(async (result, ct) =>
        {
            var profileName = result.GetValue(profileOption)!;
            var source = result.GetValue(sourceOption)!;
            var dest = result.GetValue(destOption)!;
            var guild = result.GetValue(guildOption)!;
            var dryRun = result.GetValue(dryRunOption);
            var noReactions = result.GetValue(noReactionsOption);

            var token = await ResolveTokenAsync(profileName, appDataPath);
            if (token is null) return 1;

            var logger = Log.Logger;

            using var discordClient = new DiscordRestClient(token, logger);
            var messageEndpoints = new MessageEndpoints(discordClient);
            var webhookEndpoints = new WebhookEndpoints(discordClient);
            var reactionEndpoints = new ReactionEndpoints(discordClient);
            var channelEndpoints = new ChannelEndpoints(discordClient);
            var messageMigrator = new MessageMigrator(logger);
            using var cdnHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var attachmentHandler = new AttachmentHandler(cdnHttpClient, logger);

            var engine = new MigrationEngine(
                messageEndpoints,
                webhookEndpoints,
                reactionEndpoints,
                channelEndpoints,
                messageMigrator,
                attachmentHandler,
                appDataPath,
                logger);

            // Parse optional date filters (UTC assumed when no offset is given).
            DateTimeOffset? since = null;
            var sinceRaw = result.GetValue(sinceOption);
            if (!string.IsNullOrWhiteSpace(sinceRaw))
            {
                if (!DateTimeOffset.TryParse(sinceRaw, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var s))
                {
                    Console.Error.WriteLine($"Invalid --since value: '{sinceRaw}'. Use e.g. 2024-01-01 or 2024-01-01T12:00:00Z.");
                    return 1;
                }
                since = s;
            }

            DateTimeOffset? until = null;
            var untilRaw = result.GetValue(untilOption);
            if (!string.IsNullOrWhiteSpace(untilRaw))
            {
                if (!DateTimeOffset.TryParse(untilRaw, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var u))
                {
                    Console.Error.WriteLine($"Invalid --until value: '{untilRaw}'. Use e.g. 2024-01-01 or 2024-01-01T12:00:00Z.");
                    return 1;
                }
                until = u;
            }

            var filter = new MessageFilter(
                AuthorId: result.GetValue(fromAuthorOption),
                Since: since,
                Until: until,
                ContentContains: result.GetValue(containsOption),
                AttachmentsOnly: result.GetValue(attachmentsOnlyOption),
                ExcludeBots: result.GetValue(noBotsOption));

            var options = new MigrationOptions(
                SourceChannelId: source,
                DestinationChannelId: dest,
                GuildId: guild,
                DryRun: dryRun,
                IncludeReactions: !noReactions,
                IncludePins: !result.GetValue(noPinsOption),
                IncludePolls: !result.GetValue(noPollsOption),
                IncludeStickers: !result.GetValue(noStickersOption),
                IncludeTimestamps: result.GetValue(timestampsOption),
                Filter: filter.IsActive ? filter : null);

            // Preview
            Console.WriteLine("Analyzing source channel...");
            var preview = await engine.PreviewAsync(options, ct);

            Console.WriteLine();
            Console.WriteLine($"  Messages:    {preview.MessageCount}");
            Console.WriteLine($"  Attachments: {preview.AttachmentCount} ({preview.TotalAttachmentBytes / 1024.0 / 1024.0:F1} MB)");
            Console.WriteLine($"  Oversized:   {preview.OversizedAttachments.Count}");
            Console.WriteLine($"  Est. time:   {preview.EstimatedDuration:hh\\:mm\\:ss}");

            if (preview.Warnings.Count > 0)
            {
                Console.WriteLine();
                foreach (var warning in preview.Warnings)
                {
                    Console.WriteLine($"  WARNING: {warning}");
                }
            }

            Console.WriteLine();

            if (dryRun)
            {
                Console.WriteLine("Running in dry-run mode...");
                Console.WriteLine("  Note: the time estimate and ETA reflect a real migration, not this near-instant dry run.");
            }
            else
            {
                Console.WriteLine("Starting migration...");
            }

            // Run with progress
            var progress = new Progress<MigrationProgress>(p =>
            {
                var pct = p.Total > 0 ? (int)(100.0 * p.Completed / p.Total) : 0;
                var eta = p.EstimatedRemaining is not null
                    ? $" ETA {p.EstimatedRemaining.Value:hh\\:mm\\:ss}"
                    : "";
                Console.Write($"\r  [{pct,3}%] {p.Completed}/{p.Total} migrated, {p.Failed} failed, {p.Skipped} skipped | {p.Phase}{eta}   ");
            });

            var migrationResult = await engine.RunAsync(options, progress, ct);

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Migration complete.");
            Console.WriteLine($"  Migrated: {migrationResult.TotalMigrated}");
            Console.WriteLine($"  Failed:   {migrationResult.TotalFailed}");
            Console.WriteLine(migrationResult.TotalSkipped > 0
                ? $"  Skipped:  {migrationResult.TotalSkipped}  (system messages: joins, pins, etc.)"
                : $"  Skipped:  {migrationResult.TotalSkipped}");
            Console.WriteLine($"  Duration: {migrationResult.Duration:hh\\:mm\\:ss}");

            if (migrationResult.FailedMessages.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failed messages:");
                foreach (var failed in migrationResult.FailedMessages)
                {
                    Console.WriteLine($"  - {failed.SourceMessageId}: {failed.Reason}");
                }

                return 1;
            }

            return 0;
        });

        // Resume subcommand
        var resumeCommand = new Command("resume") { Description = "Resume an interrupted migration" };
        var stateFileArg = new Argument<string>("state-file");
        var resumeProfileOption = new Option<string>("--profile") { Description = "Bot profile name", Required = true };
        resumeCommand.Arguments.Add(stateFileArg);
        resumeCommand.Options.Add(resumeProfileOption);
        resumeCommand.SetAction(async (result, ct) =>
        {
            var stateFile = result.GetValue(stateFileArg)!;
            var profileName = result.GetValue(resumeProfileOption)!;

            var state = await MigrationState.LoadAsync(stateFile);
            if (state is null)
            {
                Console.Error.WriteLine($"Could not load migration state from '{stateFile}'.");
                return 1;
            }

            var token = await ResolveTokenAsync(profileName, appDataPath);
            if (token is null) return 1;

            var logger = Log.Logger;

            using var discordClient = new DiscordRestClient(token, logger);
            var messageEndpoints = new MessageEndpoints(discordClient);
            var webhookEndpoints = new WebhookEndpoints(discordClient);
            var reactionEndpoints = new ReactionEndpoints(discordClient);
            var channelEndpoints = new ChannelEndpoints(discordClient);
            var messageMigrator = new MessageMigrator(logger);
            using var cdnHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var attachmentHandler = new AttachmentHandler(cdnHttpClient, logger);

            var engine = new MigrationEngine(
                messageEndpoints,
                webhookEndpoints,
                reactionEndpoints,
                channelEndpoints,
                messageMigrator,
                attachmentHandler,
                appDataPath,
                logger);

            Console.WriteLine($"Resuming migration {state.MigrationId}...");
            Console.WriteLine($"  Already migrated: {state.MigratedCount}");

            var progress = new Progress<MigrationProgress>(p =>
            {
                var pct = p.Total > 0 ? (int)(100.0 * p.Completed / p.Total) : 0;
                var eta = p.EstimatedRemaining is not null
                    ? $" ETA {p.EstimatedRemaining.Value:hh\\:mm\\:ss}"
                    : "";
                Console.Write($"\r  [{pct,3}%] {p.Completed}/{p.Total} migrated, {p.Failed} failed, {p.Skipped} skipped | {p.Phase}{eta}   ");
            });

            var migrationResult = await engine.ResumeAsync(state, progress, ct);

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Migration complete.");
            Console.WriteLine($"  Migrated: {migrationResult.TotalMigrated}");
            Console.WriteLine($"  Failed:   {migrationResult.TotalFailed}");
            Console.WriteLine(migrationResult.TotalSkipped > 0
                ? $"  Skipped:  {migrationResult.TotalSkipped}  (system messages: joins, pins, etc.)"
                : $"  Skipped:  {migrationResult.TotalSkipped}");
            Console.WriteLine($"  Duration: {migrationResult.Duration:hh\\:mm\\:ss}");

            if (migrationResult.FailedMessages.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failed messages:");
                foreach (var failed in migrationResult.FailedMessages)
                {
                    Console.WriteLine($"  - {failed.SourceMessageId}: {failed.Reason}");
                }

                return 1;
            }

            return 0;
        });

        command.Subcommands.Add(resumeCommand);

        return command;
    }

    private static async Task<string?> ResolveTokenAsync(string profileName, string appDataPath)
    {
        var credentialStore = CredentialStoreFactory.Create();
        var profileManager = new ProfileManager(credentialStore, appDataPath);
        var token = await profileManager.GetTokenAsync(profileName);

        if (token is null)
        {
            Console.Error.WriteLine($"Profile '{profileName}' not found or has no token.");
        }

        return token;
    }
}
