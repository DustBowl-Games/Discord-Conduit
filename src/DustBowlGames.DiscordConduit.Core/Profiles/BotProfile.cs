namespace DustBowlGames.DiscordConduit.Core.Profiles;

/// <summary>
/// Represents a named bot profile with a reference to its stored credential.
/// </summary>
public sealed class BotProfile
{
    /// <summary>The display name of the bot profile.</summary>
    public required string Name { get; init; }

    /// <summary>The key used to look up the bot token in the credential store.</summary>
    public required string TokenCredentialKey { get; init; }
}
