using System.CommandLine;
using System.Text;
using DustBowlGames.DiscordConduit.Core.Credentials;
using DustBowlGames.DiscordConduit.Core.Profiles;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class ProfileCommand
{
    private const string TokenEnvVar = "DISCORD_CONDUIT_TOKEN";

    public static Command Create(string appDataPath)
    {
        var command = new Command("profile") { Description = "Manage bot profiles" };

        // Add subcommand
        var addCommand = new Command("add")
        {
            Description =
                "Add a new bot profile. The token can be provided via --token, the " +
                $"{TokenEnvVar} environment variable, or an interactive masked prompt."
        };
        var nameArg = new Argument<string>("name");
        var tokenOption = new Option<string>("--token")
        {
            Description =
                "Bot token (optional). If omitted, the token is read from the " +
                $"{TokenEnvVar} environment variable or an interactive masked prompt. " +
                "Passing the token on the command line exposes it in process listings " +
                "and shell history, so prefer the env var or prompt."
        };
        addCommand.Arguments.Add(nameArg);
        addCommand.Options.Add(tokenOption);
        addCommand.SetAction(async (result, ct) =>
        {
            var name = result.GetValue(nameArg)!;
            var token = ResolveTokenForAdd(result.GetValue(tokenOption));

            if (string.IsNullOrWhiteSpace(token))
            {
                Console.Error.WriteLine(
                    "No token provided. Supply --token, set the " +
                    $"{TokenEnvVar} environment variable, or enter the token when prompted.");
                return 1;
            }

            Console.WriteLine($"Adding profile '{name}'...");

            var credentialStore = CredentialStoreFactory.Create();
            var profileManager = new ProfileManager(credentialStore, appDataPath);
            await profileManager.AddProfileAsync(name, token);

            Console.WriteLine("Profile added.");
            return 0;
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

            return 0;
        });

        // Remove subcommand
        var removeCommand = new Command("remove") { Description = "Remove a bot profile" };
        var removeNameArg = new Argument<string>("name");
        removeCommand.Arguments.Add(removeNameArg);
        removeCommand.SetAction(async (result, ct) =>
        {
            var name = result.GetValue(removeNameArg)!;

            var credentialStore = CredentialStoreFactory.Create();
            var profileManager = new ProfileManager(credentialStore, appDataPath);

            var profiles = await profileManager.GetProfilesAsync();
            var exists = profiles.Any(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                Console.Error.WriteLine($"Profile '{name}' not found.");
                return 1;
            }

            Console.WriteLine($"Removing profile '{name}'...");

            await profileManager.RemoveProfileAsync(name);

            Console.WriteLine("Profile removed.");
            return 0;
        });

        command.Subcommands.Add(addCommand);
        command.Subcommands.Add(listCommand);
        command.Subcommands.Add(removeCommand);

        return command;
    }

    /// <summary>
    /// Resolves the token to use when adding a profile, in priority order:
    /// (1) the explicit --token value, (2) the environment variable, (3) an
    /// interactive masked console prompt.
    /// </summary>
    private static string? ResolveTokenForAdd(string? explicitToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitToken))
        {
            return explicitToken;
        }

        var envToken = Environment.GetEnvironmentVariable(TokenEnvVar);
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            return envToken;
        }

        return PromptForToken();
    }

    /// <summary>
    /// Prompts for the bot token on the console. When stdin is redirected the
    /// token is read as a single line (so piping works); otherwise input is
    /// read key-by-key and masked so the secret is never echoed.
    /// </summary>
    private static string? PromptForToken()
    {
        if (Console.IsInputRedirected)
        {
            // Piped input (e.g. `echo $TOKEN | discordconduit profile add ...`).
            return Console.ReadLine();
        }

        Console.Write("Enter bot token: ");

        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            // Ignore other control characters (arrows, function keys, etc.).
            if (char.IsControl(key.KeyChar))
            {
                continue;
            }

            builder.Append(key.KeyChar);
            Console.Write('*');
        }

        return builder.ToString();
    }
}
