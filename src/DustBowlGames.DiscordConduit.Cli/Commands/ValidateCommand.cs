using System.CommandLine;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Credentials;
using DustBowlGames.DiscordConduit.Core.Profiles;
using DustBowlGames.DiscordConduit.Core.Validation;
using Serilog;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class ValidateCommand
{
    public static Command Create(string appDataPath)
    {
        var command = new Command("validate") { Description = "Validate bot permissions for a migration" };

        var profileOption = new Option<string>("--profile") { Description = "Bot profile name", Required = true };
        var sourceOption = new Option<string>("--source") { Description = "Source channel ID", Required = true };
        var destOption = new Option<string>("--dest") { Description = "Destination channel ID", Required = true };
        var guildOption = new Option<string>("--guild") { Description = "Guild (server) ID", Required = true };

        command.Options.Add(profileOption);
        command.Options.Add(sourceOption);
        command.Options.Add(destOption);
        command.Options.Add(guildOption);

        command.SetAction(async (result, ct) =>
        {
            var profileName = result.GetValue(profileOption)!;
            var source = result.GetValue(sourceOption)!;
            var dest = result.GetValue(destOption)!;
            var guild = result.GetValue(guildOption)!;

            var credentialStore = CredentialStoreFactory.Create();
            var profileManager = new ProfileManager(credentialStore, appDataPath);
            var token = await profileManager.GetTokenAsync(profileName);

            if (token is null)
            {
                Console.Error.WriteLine($"Profile '{profileName}' not found or has no token.");
                return 1;
            }

            var logger = Log.Logger;

            Console.WriteLine($"Validating permissions for profile '{profileName}'...");
            Console.WriteLine($"  Source:      {source}");
            Console.WriteLine($"  Destination: {dest}");
            Console.WriteLine($"  Guild:       {guild}");

            using var discordClient = new DiscordRestClient(token, logger);
            var validator = new PermissionValidator(discordClient, logger);
            var checkResult = await validator.ValidateAsync(source, dest, guild, ct);

            Console.WriteLine();

            if (checkResult.IsValid)
            {
                Console.WriteLine("Validation passed. All required permissions are present.");
                return 0;
            }

            Console.Error.WriteLine($"Validation failed with {checkResult.Issues.Count} issue(s):");
            Console.Error.WriteLine();

            foreach (var issue in checkResult.Issues)
            {
                Console.Error.WriteLine($"  [{issue.Channel.ToUpperInvariant()}] {issue.Permission}");
                Console.Error.WriteLine($"    {issue.Description}");
                Console.Error.WriteLine();
            }

            return 1;
        });

        return command;
    }
}
