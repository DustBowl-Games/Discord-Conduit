namespace DustBowlGames.DiscordConduit.Core.IO;

/// <summary>
/// Helpers for creating application data locations with safe permissions.
/// </summary>
public static class SecurePaths
{
    /// <summary>
    /// Creates the directory (and any parents) if it does not exist and, on Unix-like systems,
    /// restricts it to the owner (mode 0700). The application data directory holds migration
    /// state, per-migration logs, reports, and profile metadata; restricting the top-level
    /// directory prevents other local users from reading that data on shared hosts. On Windows
    /// the call is a no-op beyond directory creation (ACLs already default to the user profile).
    /// </summary>
    /// <param name="path">The directory path to create and secure.</param>
    /// <returns>The same <paramref name="path"/>, for convenient chaining.</returns>
    public static string CreateOwnerOnlyDirectory(string path)
    {
        Directory.CreateDirectory(path);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Best effort: some filesystems (e.g. certain network mounts) don't support Unix
                // modes. Failing to tighten permissions must not prevent the app from starting.
            }
        }

        return path;
    }
}
