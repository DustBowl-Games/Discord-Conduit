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
    public static Command Create(string appDataPath)
    {
        var command = new Command("bot") { Description = "Run the Discord Conduit bot for slash commands" };

        var startCommand = new Command("start") { Description = "Start the bot (Ctrl+C to stop)" };
        var profileOption = new Option<string>("--profile") { Description = "Bot profile name", Required = true };
        startCommand.Options.Add(profileOption);

        startCommand.SetAction(async (result, ct) =>
        {
            var profileName = result.GetValue(profileOption)!;

            var credentialStore = CredentialStoreFactory.Create();
            var profileManager = new ProfileManager(credentialStore, appDataPath);
            var token = await profileManager.GetTokenAsync(profileName);

            if (token is null)
            {
                Console.Error.WriteLine($"Profile '{profileName}' not found or has no token.");
                return;
            }

            var logger = Log.Logger;

            using var restClient = new DiscordRestClient(token, logger);
            using var cdnHttpClient = new HttpClient();

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
            Console.WriteLine($"Connecting to Discord as profile '{profileName}'...");

            await gateway.ConnectAsync(ct);

            if (gateway.ApplicationId is null)
            {
                Console.Error.WriteLine("Failed to obtain application ID from gateway. Check your bot token.");
                return;
            }

            Console.WriteLine($"Connected. Application ID: {gateway.ApplicationId}");

            // Create move handler
            var moveHandler = new MoveCommandHandler(
                messageEndpoints, webhookEndpoints, channelEndpoints, interactionEndpoints,
                messageMigrator, attachmentHandler,
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
                    await moveHandler.HandleComponentInteractionAsync(interaction);
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
        });

        command.Subcommands.Add(startCommand);
        return command;
    }
}
