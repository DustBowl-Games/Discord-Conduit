using System.Diagnostics;
using System.Runtime.Versioning;

namespace DustBowlGames.DiscordConduit.Core.Credentials;

/// <summary>
/// Credential store backed by the macOS Keychain via the <c>security</c> CLI tool.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsCredentialStore : ICredentialStore
{
    private const string KeyPrefix = "DiscordConduit:";
    private const string Account = "DiscordConduit";

    /// <inheritdoc />
    public async Task SaveAsync(string key, string secret)
    {
        var targetKey = KeyPrefix + key;
        // Pass secret via stdin to avoid exposure in process args (visible via ps)
        var result = await RunSecurityAsync(
            stdinData: secret,
            "add-generic-password", "-a", Account, "-s", targetKey, "-w", "-U").ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"security add-generic-password failed (exit {result.ExitCode}): {result.StdErr}");
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key)
    {
        var targetKey = KeyPrefix + key;
        var result = await RunSecurityAsync(
            args: ["find-generic-password", "-a", Account, "-s", targetKey, "-w"]).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        return result.StdOut.Trim();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key)
    {
        var targetKey = KeyPrefix + key;
        await RunSecurityAsync(
            args: ["delete-generic-password", "-a", Account, "-s", targetKey]).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListKeysAsync(string prefix)
    {
        // Profile names are tracked separately by ProfileManager (profiles.json).
        // We don't enumerate the keychain — dump-keychain exposes the entire user
        // keychain which is both a security concern and triggers macOS permission dialogs.
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private static async Task<ProcessResult> RunSecurityAsync(string? stdinData = null, params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "security",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinData is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();

        if (stdinData is not null)
        {
            await process.StandardInput.WriteAsync(stdinData).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        var stdOut = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stdErr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
