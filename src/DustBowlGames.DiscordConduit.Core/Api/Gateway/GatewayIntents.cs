namespace DustBowlGames.DiscordConduit.Core.Api.Gateway;

/// <summary>
/// Bitfield of Discord gateway intents that control which events the bot receives.
/// </summary>
[Flags]
public enum GatewayIntents
{
    /// <summary>Events about guild creation, updates, deletion, roles, and channels.</summary>
    Guilds = 1 << 0,

    /// <summary>Events about messages in guild text channels.</summary>
    GuildMessages = 1 << 9,

    /// <summary>Grants access to message content fields (content, embeds, attachments, components).</summary>
    MessageContent = 1 << 15
}
