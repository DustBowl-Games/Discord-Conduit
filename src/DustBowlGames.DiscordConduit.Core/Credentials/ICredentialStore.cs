namespace DustBowlGames.DiscordConduit.Core.Credentials;

/// <summary>
/// Abstraction for securely storing and retrieving credentials.
/// All keys are prefixed with "DiscordConduit:" internally.
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Saves a secret under the specified key.
    /// </summary>
    /// <param name="key">The key to store the secret under.</param>
    /// <param name="secret">The secret value to store.</param>
    Task SaveAsync(string key, string secret);

    /// <summary>
    /// Retrieves the secret stored under the specified key.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The secret value, or <c>null</c> if the key does not exist.</returns>
    Task<string?> GetAsync(string key);

    /// <summary>
    /// Deletes the secret stored under the specified key.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    Task DeleteAsync(string key);

    /// <summary>
    /// Lists all keys that begin with the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to filter keys by.</param>
    /// <returns>A read-only list of matching keys.</returns>
    Task<IReadOnlyList<string>> ListKeysAsync(string prefix);
}
