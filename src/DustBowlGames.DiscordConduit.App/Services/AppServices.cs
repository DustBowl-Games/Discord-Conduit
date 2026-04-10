using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
using DustBowlGames.DiscordConduit.Core.Api.Gateway;
using DustBowlGames.DiscordConduit.Core.Commands;
using DustBowlGames.DiscordConduit.Core.Credentials;
using DustBowlGames.DiscordConduit.Core.Migration;
using DustBowlGames.DiscordConduit.Core.Profiles;
using DustBowlGames.DiscordConduit.Core.Validation;
using Serilog;

namespace DustBowlGames.DiscordConduit.App.Services;

/// <summary>
/// Central service container that manages the lifecycle of Core services.
/// Created once at app startup, provides credential store and profile manager.
/// Discord-specific services are created when a bot profile connects.
/// </summary>
public sealed class AppServices : IDisposable
{
    public ICredentialStore CredentialStore { get; }
    public ProfileManager ProfileManager { get; }
    public string AppDataPath { get; }

    private HttpClient? _cdnHttpClient;

    // These are created on Connect and disposed on Disconnect
    public DiscordRestClient? RestClient { get; private set; }
    public GuildEndpoints? Guilds { get; private set; }
    public ChannelEndpoints? Channels { get; private set; }
    public MessageEndpoints? Messages { get; private set; }
    public WebhookEndpoints? Webhooks { get; private set; }
    public ReactionEndpoints? Reactions { get; private set; }
    public CommandEndpoints? Commands { get; private set; }
    public InteractionEndpoints? Interactions { get; private set; }
    public MigrationEngine? Migration { get; private set; }
    public PermissionValidator? Validator { get; private set; }
    public DiscordGatewayClient? GatewayClient { get; private set; }
    public MoveCommandHandler? MoveHandler { get; private set; }

    public bool IsConnected => RestClient is not null;
    public bool IsBotRunning => GatewayClient?.IsConnected == true;

    public AppServices()
    {
        AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiscordConduit");
        Directory.CreateDirectory(AppDataPath);

        CredentialStore = CredentialStoreFactory.Create();
        ProfileManager = new ProfileManager(CredentialStore, AppDataPath);
    }

    /// <summary>
    /// Connects to Discord using the provided bot token, creating all endpoint and engine instances.
    /// Starts the gateway bot for slash command support.
    /// </summary>
    public async Task ConnectAsync(string botToken)
    {
        Disconnect();

        RestClient = new DiscordRestClient(botToken, Log.Logger);

        Guilds = new GuildEndpoints(RestClient);
        Channels = new ChannelEndpoints(RestClient);
        Messages = new MessageEndpoints(RestClient);
        Webhooks = new WebhookEndpoints(RestClient);
        Reactions = new ReactionEndpoints(RestClient);
        Commands = new CommandEndpoints(RestClient);
        Interactions = new InteractionEndpoints(RestClient);

        var messageMigrator = new MessageMigrator(Log.Logger);
        _cdnHttpClient = new HttpClient();
        var attachmentHandler = new AttachmentHandler(_cdnHttpClient, Log.Logger);

        Migration = new MigrationEngine(
            Messages, Webhooks, Reactions, Channels,
            messageMigrator, attachmentHandler,
            AppDataPath, Log.Logger);

        Validator = new PermissionValidator(RestClient, Log.Logger);

        // Start gateway bot
        GatewayClient = new DiscordGatewayClient(botToken, RestClient, Log.Logger);
        await GatewayClient.ConnectAsync(CancellationToken.None);

        // Register slash commands once we have the application ID
        if (GatewayClient.ApplicationId is not null)
        {
            MoveHandler = new MoveCommandHandler(
                Messages, Webhooks, Channels, Interactions,
                messageMigrator, attachmentHandler,
                GatewayClient.ApplicationId, Log.Logger);

            // Register commands with Discord
            var commandDefs = MoveCommandHandler.GetCommandDefinitions();
            await Commands.BulkUpsertGlobalCommandsAsync(GatewayClient.ApplicationId, commandDefs);
            Log.Logger.Information("Registered {Count} slash commands", commandDefs.Count);

            // Wire interaction events
            GatewayClient.OnInteractionCreate += HandleInteractionAsync;
        }
    }

    private async Task HandleInteractionAsync(InteractionCreateEvent interaction)
    {
        if (MoveHandler is null) return;

        // Type 2 = Application Command, Type 3 = Message Component
        if (interaction.Type == 2)
        {
            await MoveHandler.HandleInteractionAsync(interaction);
        }
        else if (interaction.Type == 3)
        {
            await MoveHandler.HandleComponentInteractionAsync(interaction);
        }
    }

    /// <summary>
    /// Disconnects from Discord, disposing the REST client and gateway.
    /// </summary>
    public void Disconnect()
    {
        if (GatewayClient is not null)
        {
            GatewayClient.OnInteractionCreate -= HandleInteractionAsync;
            try { GatewayClient.DisconnectAsync().GetAwaiter().GetResult(); }
            catch { /* best effort */ }
            GatewayClient = null;
        }

        MoveHandler = null;
        _cdnHttpClient?.Dispose();
        _cdnHttpClient = null;
        RestClient?.Dispose();
        RestClient = null;
        Guilds = null;
        Channels = null;
        Messages = null;
        Webhooks = null;
        Reactions = null;
        Commands = null;
        Interactions = null;
        Migration = null;
        Validator = null;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
