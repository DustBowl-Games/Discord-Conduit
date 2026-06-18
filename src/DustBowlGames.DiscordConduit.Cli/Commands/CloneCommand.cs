using System.CommandLine;
using System.Globalization;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Credentials;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Profiles;
using Serilog;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class CloneCommand
{
    public static Command Create(string appDataPath)
    {
        var command = new Command("clone")
        {
            Description = "Clone a channel (or an entire category) into a destination server",
        };

        var profileOption = new Option<string>("--profile") { Description = "Bot profile name", Required = true };
        var sourceOption = new Option<string>("--source") { Description = "Source channel (or category) ID to clone", Required = true };
        var destGuildOption = new Option<string>("--dest-guild") { Description = "Destination guild (server) ID — may be a different server (the bot must be in both)", Required = true };
        var parentOption = new Option<string?>("--parent") { Description = "Destination category ID to place the cloned channel under (single-channel mode)" };
        var categoryOption = new Option<bool>("--category") { Description = "Treat --source as a category and clone all of its text channels" };
        var dryRunOption = new Option<bool>("--dry-run") { Description = "Create the channel(s) but don't post messages" };
        var noReactionsOption = new Option<bool>("--no-reactions") { Description = "Skip reaction migration" };

        var fromAuthorOption = new Option<string?>("--from-author") { Description = "Only clone messages from this author (user ID)" };
        var sinceOption = new Option<string?>("--since") { Description = "Only clone messages on/after this date/time" };
        var untilOption = new Option<string?>("--until") { Description = "Only clone messages on/before this date/time" };
        var containsOption = new Option<string?>("--contains") { Description = "Only clone messages whose text contains this (case-insensitive)" };
        var attachmentsOnlyOption = new Option<bool>("--attachments-only") { Description = "Only clone messages that have attachments" };
        var noBotsOption = new Option<bool>("--no-bots") { Description = "Exclude messages authored by bots" };

        foreach (var o in new Option[]
        {
            profileOption, sourceOption, destGuildOption, parentOption, categoryOption, dryRunOption,
            noReactionsOption, fromAuthorOption, sinceOption, untilOption, containsOption,
            attachmentsOnlyOption, noBotsOption,
        })
        {
            command.Options.Add(o);
        }

        command.SetAction(async (result, ct) =>
        {
            var profileName = result.GetValue(profileOption)!;
            var source = result.GetValue(sourceOption)!;
            var destGuild = result.GetValue(destGuildOption)!;
            var parent = result.GetValue(parentOption);
            var category = result.GetValue(categoryOption);
            var dryRun = result.GetValue(dryRunOption);
            var includeReactions = !result.GetValue(noReactionsOption);

            if (!TryParseDate(result.GetValue(sinceOption), "--since", out var since)) return 1;
            if (!TryParseDate(result.GetValue(untilOption), "--until", out var until)) return 1;

            var filter = new MessageFilter(
                AuthorId: result.GetValue(fromAuthorOption),
                Since: since,
                Until: until,
                ContentContains: result.GetValue(containsOption),
                AttachmentsOnly: result.GetValue(attachmentsOnlyOption),
                ExcludeBots: result.GetValue(noBotsOption));
            var activeFilter = filter.IsActive ? filter : null;

            var token = await ResolveTokenAsync(profileName, appDataPath);
            if (token is null) return 1;

            var logger = Log.Logger;
            using var client = new DiscordRestClient(token, logger);
            using var cdnHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

            var channelEndpoints = new ChannelEndpoints(client);
            var engine = new MigrationEngine(
                new MessageEndpoints(client),
                new WebhookEndpoints(client),
                new ReactionEndpoints(client),
                channelEndpoints,
                new MessageMigrator(logger),
                new AttachmentHandler(cdnHttpClient, logger),
                appDataPath,
                logger);
            var cloner = new ChannelCloner(channelEndpoints, engine, logger);

            var progress = new Progress<MigrationProgress>(p =>
                Console.Write($"\r  [{p.Phase}] {p.Completed}/{p.Total} migrated, {p.Failed} failed   "));

            if (category)
            {
                Console.WriteLine($"Cloning category {source} into server {destGuild}...");
                var r = await cloner.CloneCategoryAsync(source, destGuild, includeReactions, dryRun, activeFilter, progress, ct);
                Console.WriteLine();
                Console.WriteLine($"Cloned category '{r.Name}' -> {r.NewCategoryId} ({r.Channels.Count} channel(s)):");
                foreach (var c in r.Channels)
                    Console.WriteLine($"  #{c.Name}: {c.Migration.TotalMigrated} migrated, {c.Migration.TotalFailed} failed");
            }
            else
            {
                Console.WriteLine($"Cloning channel {source} into server {destGuild}...");
                var r = await cloner.CloneChannelAsync(source, destGuild, parent, includeReactions, dryRun, activeFilter, progress, ct);
                Console.WriteLine();
                Console.WriteLine($"Cloned '#{r.Name}' -> {r.NewChannelId}: {r.Migration.TotalMigrated} migrated, {r.Migration.TotalFailed} failed");
            }

            return 0;
        });

        return command;
    }

    private static bool TryParseDate(string? raw, string flag, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            value = dt;
            return true;
        }

        Console.Error.WriteLine($"Invalid {flag} value: '{raw}'. Use e.g. 2024-01-01 or 2024-01-01T12:00:00Z.");
        return false;
    }

    private static async Task<string?> ResolveTokenAsync(string profileName, string appDataPath)
    {
        var credentialStore = CredentialStoreFactory.Create();
        var profileManager = new ProfileManager(credentialStore, appDataPath);
        var token = await profileManager.GetTokenAsync(profileName);

        if (token is null)
            Console.Error.WriteLine($"Profile '{profileName}' not found or has no token.");

        return token;
    }
}
