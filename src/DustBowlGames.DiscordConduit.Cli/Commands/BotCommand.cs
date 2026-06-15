using System.CommandLine;
using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Gateway;
using DustBowlGames.DiscordConduit.Core.Commands;
using DustBowlGames.DiscordConduit.Core.Credentials;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Profiles;
using Serilog;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class BotCommand
{
    /// <summary>
    /// Environment variable that supplies the bot token when running headless
    /// (e.g. in a container or Kubernetes pod) where there is no OS credential
    /// store or saved profile.
    /// </summary>
    private const string TokenEnvVar = "DISCORD_CONDUIT_TOKEN";

    public static Command Create(string appDataPath)
    {
        var command = new Command("bot") { Description = "Run the Discord Conduit bot for slash commands" };

        var startCommand = new Command("start") { Description = "Start the bot (Ctrl+C to stop)" };

        // --profile is optional: in a container/k8s there is no OS credential
        // store and no saved profile, so the token comes from --token-file or
        // the DISCORD_CONDUIT_TOKEN environment variable instead.
        var profileOption = new Option<string>("--profile")
        {
            Description =
                "Bot profile name (resolved via the OS credential store). Optional. " +
                $"If omitted, the token is read from --token-file or the {TokenEnvVar} " +
                "environment variable, which is the recommended way to run headless " +
                "(container / Kubernetes).",
        };

        // --token-file points at a file whose contents are the token. This is the
        // canonical way to consume a Kubernetes/Docker mounted secret without
        // exposing the token in process listings or environment dumps.
        var tokenFileOption = new Option<string>("--token-file")
        {
            Description =
                "Path to a file containing the bot token (its contents are read and " +
                "trimmed). Intended for mounted Kubernetes/Docker secrets. Takes " +
                $"precedence over the {TokenEnvVar} environment variable and --profile.",
        };

        startCommand.Options.Add(profileOption);
        startCommand.Options.Add(tokenFileOption);

        startCommand.SetAction(async (result, ct) =>
        {
            var profileName = result.GetValue(profileOption);
            var tokenFile = result.GetValue(tokenFileOption);

            // Resolution order:
            //   (a) --token-file  -> read & trim the file contents
            //   (b) DISCORD_CONDUIT_TOKEN env var (if set / non-empty)
            //   (c) --profile     -> existing credential-store / ProfileManager path
            // The first source that yields a non-empty token wins. If none does,
            // we report an error to stderr and return a non-zero exit code.
            string? token = null;
            string source;

            if (!string.IsNullOrWhiteSpace(tokenFile))
            {
                if (!File.Exists(tokenFile))
                {
                    Console.Error.WriteLine($"Token file '{tokenFile}' does not exist.");
                    return 1;
                }

                try
                {
                    token = (await File.ReadAllTextAsync(tokenFile, ct)).Trim();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Failed to read token file '{tokenFile}': {ex.Message}");
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    Console.Error.WriteLine($"Token file '{tokenFile}' is empty.");
                    return 1;
                }

                source = $"token file '{tokenFile}'";
            }
            else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TokenEnvVar)))
            {
                token = Environment.GetEnvironmentVariable(TokenEnvVar)!.Trim();
                source = $"{TokenEnvVar} environment variable";
            }
            else if (!string.IsNullOrWhiteSpace(profileName))
            {
                var credentialStore = CredentialStoreFactory.Create();
                var profileManager = new ProfileManager(credentialStore, appDataPath);
                token = await profileManager.GetTokenAsync(profileName);

                if (token is null)
                {
                    Console.Error.WriteLine($"Profile '{profileName}' not found or has no token.");
                    return 1;
                }

                source = $"profile '{profileName}'";
            }
            else
            {
                Console.Error.WriteLine(
                    "No bot token available. Provide one via --token-file <path>, the " +
                    $"{TokenEnvVar} environment variable, or --profile <name>.");
                return 1;
            }

            var logger = Log.Logger;

            using var restClient = new DiscordRestClient(token, logger);
            using var cdnHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

            var messageEndpoints = new MessageEndpoints(restClient);
            var webhookEndpoints = new WebhookEndpoints(restClient);
            var reactionEndpoints = new ReactionEndpoints(restClient);
            var channelEndpoints = new ChannelEndpoints(restClient);
            var commandEndpoints = new CommandEndpoints(restClient);
            var interactionEndpoints = new InteractionEndpoints(restClient);
            var messageMigrator = new MessageMigrator(logger);
            var attachmentHandler = new AttachmentHandler(cdnHttpClient, logger);

            // Connect gateway
            var gateway = new DiscordGatewayClient(token, restClient, logger);
            Console.WriteLine($"Connecting to Discord (token from {source})...");

            await gateway.ConnectAsync(ct);

            if (gateway.ApplicationId is null)
            {
                Console.Error.WriteLine("Failed to obtain application ID from gateway. Check your bot token.");
                return 1;
            }

            Console.WriteLine($"Connected. Application ID: {gateway.ApplicationId}");

            // Create move handler
            var stateStore = new InteractionStateStore();
            var moveHandler = new MoveCommandHandler(
                messageEndpoints, webhookEndpoints, channelEndpoints, interactionEndpoints,
                messageMigrator, attachmentHandler,
                stateStore,
                gateway.ApplicationId, logger);

            // Register commands
            var commandDefs = MoveCommandHandler.GetCommandDefinitions();
            await commandEndpoints.BulkUpsertGlobalCommandsAsync(gateway.ApplicationId, commandDefs);
            Console.WriteLine($"Registered {commandDefs.Count} slash commands.");

            // Wire interaction handler
            gateway.OnInteractionCreate += async interaction =>
            {
                if (interaction.Type == 2)
                    await moveHandler.HandleInteractionAsync(interaction);
                else if (interaction.Type == 3)
                    await moveHandler.HandleComponentAsync(interaction);
                else if (interaction.Type == 5)
                    await moveHandler.HandleModalSubmitAsync(interaction);
            };

            Console.WriteLine("Bot is running. Press Ctrl+C to stop.");
            Console.WriteLine();

            // Wait until cancelled
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                // Expected on Ctrl+C
            }

            Console.WriteLine("Shutting down...");
            await gateway.DisconnectAsync();
            Console.WriteLine("Bot stopped.");

            return 0;
        });

        command.Subcommands.Add(startCommand);
        return command;
    }
}
