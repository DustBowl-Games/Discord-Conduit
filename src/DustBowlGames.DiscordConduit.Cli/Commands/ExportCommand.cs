using System.CommandLine;
using System.Globalization;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Credentials;
using DustBowlGames.DiscordConduit.Core.Export;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Profiles;
using Serilog;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class ExportCommand
{
    public static Command Create(string appDataPath)
    {
        var command = new Command("export")
        {
            Description = "Export a channel's messages to a file (read-only — nothing is posted)",
        };

        var profileOption = new Option<string>("--profile") { Description = "Bot profile name", Required = true };
        var channelOption = new Option<string>("--channel") { Description = "Channel ID to export", Required = true };
        var formatOption = new Option<string?>("--format") { Description = "Output format: html | json | csv | txt (default: html)" };
        var outputOption = new Option<string?>("--output") { Description = "Output file path (default: export-<channel>.<ext>)" };

        var fromAuthorOption = new Option<string?>("--from-author") { Description = "Only export messages from this author (user ID)" };
        var sinceOption = new Option<string?>("--since") { Description = "Only export messages on/after this date/time" };
        var untilOption = new Option<string?>("--until") { Description = "Only export messages on/before this date/time" };
        var containsOption = new Option<string?>("--contains") { Description = "Only export messages whose text contains this (case-insensitive)" };
        var attachmentsOnlyOption = new Option<bool>("--attachments-only") { Description = "Only export messages that have attachments" };
        var noBotsOption = new Option<bool>("--no-bots") { Description = "Exclude messages authored by bots" };

        command.Options.Add(profileOption);
        command.Options.Add(channelOption);
        command.Options.Add(formatOption);
        command.Options.Add(outputOption);
        command.Options.Add(fromAuthorOption);
        command.Options.Add(sinceOption);
        command.Options.Add(untilOption);
        command.Options.Add(containsOption);
        command.Options.Add(attachmentsOnlyOption);
        command.Options.Add(noBotsOption);

        command.SetAction(async (result, ct) =>
        {
            var profileName = result.GetValue(profileOption)!;
            var channel = result.GetValue(channelOption)!;

            var (format, ext, formatOk) = ParseFormat(result.GetValue(formatOption));
            if (!formatOk)
                return 1;

            if (!TryParseDate(result.GetValue(sinceOption), "--since", out var since)) return 1;
            if (!TryParseDate(result.GetValue(untilOption), "--until", out var until)) return 1;

            var filter = new MessageFilter(
                AuthorId: result.GetValue(fromAuthorOption),
                Since: since,
                Until: until,
                ContentContains: result.GetValue(containsOption),
                AttachmentsOnly: result.GetValue(attachmentsOnlyOption),
                ExcludeBots: result.GetValue(noBotsOption));

            var output = result.GetValue(outputOption);
            if (string.IsNullOrWhiteSpace(output))
                output = $"export-{channel}.{ext}";

            var token = await ResolveTokenAsync(profileName, appDataPath);
            if (token is null)
                return 1;

            var logger = Log.Logger;
            using var client = new DiscordRestClient(token, logger);
            var exporter = new ChannelExporter(new MessageEndpoints(client), logger);

            Console.WriteLine($"Exporting channel {channel} as {format}...");
            var progress = new Progress<int>(n => Console.Write($"\r  Fetched {n} messages..."));

            var count = await exporter.ExportAsync(
                channel, format, output, filter.IsActive ? filter : null, progress, ct);

            Console.WriteLine();
            Console.WriteLine($"Exported {count} message(s) to {output}");
            return 0;
        });

        return command;
    }

    private static (ExportFormat Format, string Extension, bool Ok) ParseFormat(string? raw)
    {
        switch ((raw ?? "html").Trim().ToLowerInvariant())
        {
            case "html": return (ExportFormat.Html, "html", true);
            case "json": return (ExportFormat.Json, "json", true);
            case "csv": return (ExportFormat.Csv, "csv", true);
            case "txt" or "text": return (ExportFormat.Text, "txt", true);
            default:
                Console.Error.WriteLine($"Invalid --format value: '{raw}'. Use html, json, csv, or txt.");
                return (ExportFormat.Html, "html", false);
        }
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
