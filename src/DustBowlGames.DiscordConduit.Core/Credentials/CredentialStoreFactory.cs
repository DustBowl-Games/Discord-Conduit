namespace DustBowlGames.DiscordConduit.Core.Credentials;

/// <summary>
/// Factory that creates the appropriate <see cref="ICredentialStore"/> for the current platform.
/// </summary>
public static class CredentialStoreFactory
{
    /// <summary>
    /// Creates an <see cref="ICredentialStore"/> appropriate for the current operating system.
    /// </summary>
    /// <returns>A platform-specific credential store instance.</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when the current operating system is not supported.
    /// </exception>
    public static ICredentialStore Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsCredentialStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxCredentialStore();
        }

        throw new PlatformNotSupportedException(
            "No credential store implementation is available for this operating system.");
    }
}
