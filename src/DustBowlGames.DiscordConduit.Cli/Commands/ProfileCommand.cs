using System.CommandLine;
using DustBowlGames.DiscordConduit.Core.Credentials;
using DustBowlGames.DiscordConduit.Core.Profiles;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class ProfileCommand
{
    public static Command Create(string appDataPath)
    {
        var command = new Command("profile") { Description = "Manage bot profiles" };

        // Add subcommand
        var addCommand = new Command("add") { Description = "Add a new bot profile" };
        var nameArg = new Argument<string>("name");
        var tokenOption = new Option<string>("--token") { Description = "Bot token", Required = true };
        addCommand.Arguments.Add(nameArg);
        addCommand.Options.Add(tokenOption);
        addCommand.SetAction(async (result, ct) =>
        {
            var name = result.GetValue(nameArg)!;
            var token = result.GetValue(tokenOption)!;

            Console.WriteLine($"Adding profile '{name}'...");

            var credentialStore = CredentialStoreFactory.Create();
            var profileManager = new ProfileManager(credentialStore, appDataPath);
            await profileManager.AddProfileAsync(name, token);

            Console.WriteLine("Profile added.");
        });

        // List subcommand
        var listCommand = new Command("list") { Description = "List all bot profiles" };
        listCommand.SetAction(async (_, ct) =>
        {
            var credentialStore = CredentialStoreFactory.Create();
            var profileManager = new ProfileManager(credentialStore, appDataPath);
            var profiles = await profileManager.GetProfilesAsync();

            Console.WriteLine("Profiles:");
            if (profiles.Count == 0)
            {
                Console.WriteLine("  (none)");
            }
            else
            {
                foreach (var profile in profiles)
                {
                    Console.WriteLine($"  - {profile.Name}");
                }
            }
        });

        // Remove subcommand
        var removeCommand = new Command("remove") { Description = "Remove a bot profile" };
        var removeNameArg = new Argument<string>("name");
        removeCommand.Arguments.Add(removeNameArg);
        removeCommand.SetAction(async (result, ct) =>
        {
            var name = result.GetValue(removeNameArg)!;

            Console.WriteLine($"Removing profile '{name}'...");

            var credentialStore = CredentialStoreFactory.Create();
            var profileManager = new ProfileManager(credentialStore, appDataPath);
            await profileManager.RemoveProfileAsync(name);

            Console.WriteLine("Profile removed.");
        });

        command.Subcommands.Add(addCommand);
        command.Subcommands.Add(listCommand);
        command.Subcommands.Add(removeCommand);

        return command;
    }
}
