using DustBowlGames.DiscordConduit.Core.Api;
using DustBowlGames.DiscordConduit.Core.Api.Endpoints;
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

    // These are created on Connect and disposed on Disconnect
    public DiscordRestClient? RestClient { get; private set; }
    public GuildEndpoints? Guilds { get; private set; }
    public ChannelEndpoints? Channels { get; private set; }
    public MessageEndpoints? Messages { get; private set; }
    public WebhookEndpoints? Webhooks { get; private set; }
    public ReactionEndpoints? Reactions { get; private set; }
    public MigrationEngine? Migration { get; private set; }
    public PermissionValidator? Validator { get; private set; }

    public bool IsConnected => RestClient is not null;

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
    /// </summary>
    public void Connect(string botToken)
    {
        Disconnect();

        RestClient = new DiscordRestClient(botToken, Log.Logger);

        Guilds = new GuildEndpoints(RestClient);
        Channels = new ChannelEndpoints(RestClient);
        Messages = new MessageEndpoints(RestClient);
        Webhooks = new WebhookEndpoints(RestClient);
        Reactions = new ReactionEndpoints(RestClient);

        var messageMigrator = new MessageMigrator(Log.Logger);
        var attachmentHandler = new AttachmentHandler(new HttpClient(), Log.Logger);

        Migration = new MigrationEngine(
            Messages, Webhooks, Reactions, Channels,
            messageMigrator, attachmentHandler,
            AppDataPath, Log.Logger);

        Validator = new PermissionValidator(RestClient, Log.Logger);
    }

    /// <summary>
    /// Disconnects from Discord, disposing the REST client.
    /// </summary>
    public void Disconnect()
    {
        RestClient?.Dispose();
        RestClient = null;
        Guilds = null;
        Channels = null;
        Messages = null;
        Webhooks = null;
        Reactions = null;
        Migration = null;
        Validator = null;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
