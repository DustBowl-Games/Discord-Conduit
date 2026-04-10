using System.Text.Json;
using DustBowlGames.DiscordConduit.Core.Credentials;

namespace DustBowlGames.DiscordConduit.Core.Profiles;

/// <summary>
/// Manages named bot profiles, storing metadata in a JSON index file
/// and tokens in the platform credential store.
/// </summary>
public sealed class ProfileManager
{
    private const string TokenKeyPrefix = "bot:";
    private readonly ICredentialStore _credentialStore;
    private readonly string _profilesFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Creates a new <see cref="ProfileManager"/>.
    /// </summary>
    /// <param name="credentialStore">The credential store used to persist bot tokens.</param>
    /// <param name="appDataPath">The application data directory where the profile index is stored.</param>
    public ProfileManager(ICredentialStore credentialStore, string appDataPath)
    {
        _credentialStore = credentialStore;
        _profilesFilePath = Path.Combine(appDataPath, "profiles.json");
    }

    /// <summary>
    /// Adds a new bot profile, storing the token securely and recording the profile metadata.
    /// </summary>
    /// <param name="name">The name of the profile.</param>
    /// <param name="token">The bot token to store securely.</param>
    public async Task AddProfileAsync(string name, string token)
    {
        var credentialKey = TokenKeyPrefix + name;
        await _credentialStore.SaveAsync(credentialKey, token).ConfigureAwait(false);

        var profiles = await LoadProfilesAsync().ConfigureAwait(false);

        // Remove existing profile with the same name if present.
        profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        profiles.Add(new BotProfile
        {
            Name = name,
            TokenCredentialKey = credentialKey,
        });

        await SaveProfilesAsync(profiles).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a bot profile, deleting both the credential and the index entry.
    /// </summary>
    /// <param name="name">The name of the profile to remove.</param>
    public async Task RemoveProfileAsync(string name)
    {
        var credentialKey = TokenKeyPrefix + name;
        await _credentialStore.DeleteAsync(credentialKey).ConfigureAwait(false);

        var profiles = await LoadProfilesAsync().ConfigureAwait(false);
        profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        await SaveProfilesAsync(profiles).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns all registered bot profiles.
    /// </summary>
    /// <returns>A read-only list of bot profiles.</returns>
    public async Task<IReadOnlyList<BotProfile>> GetProfilesAsync()
    {
        return await LoadProfilesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the bot token for the specified profile from the credential store.
    /// </summary>
    /// <param name="profileName">The name of the profile whose token to retrieve.</param>
    /// <returns>The bot token, or <c>null</c> if not found.</returns>
    public async Task<string?> GetTokenAsync(string profileName)
    {
        var credentialKey = TokenKeyPrefix + profileName;
        return await _credentialStore.GetAsync(credentialKey).ConfigureAwait(false);
    }

    private async Task<List<BotProfile>> LoadProfilesAsync()
    {
        if (!File.Exists(_profilesFilePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_profilesFilePath);
        return await JsonSerializer.DeserializeAsync<List<BotProfile>>(stream, JsonOptions).ConfigureAwait(false)
               ?? [];
    }

    private async Task SaveProfilesAsync(List<BotProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_profilesFilePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_profilesFilePath);
        await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions).ConfigureAwait(false);
    }
}
