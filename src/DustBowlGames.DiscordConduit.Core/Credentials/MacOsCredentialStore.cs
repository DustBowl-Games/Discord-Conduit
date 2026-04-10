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
        var result = await RunSecurityAsync(
            "add-generic-password",
            $"-a \"{Account}\" -s \"{targetKey}\" -w \"{secret}\" -U").ConfigureAwait(false);

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
            "find-generic-password",
            $"-a \"{Account}\" -s \"{targetKey}\" -w").ConfigureAwait(false);

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
            "delete-generic-password",
            $"-a \"{Account}\" -s \"{targetKey}\"").ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListKeysAsync(string prefix)
    {
        var fullPrefix = KeyPrefix + prefix;
        var result = await RunSecurityAsync("dump-keychain", string.Empty).ConfigureAwait(false);
        var keys = new List<string>();

        if (result.ExitCode != 0)
        {
            return keys;
        }

        // Parse output lines looking for "svce" (service) entries matching our prefix.
        foreach (var line in result.StdOut.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("\"svce\"", StringComparison.Ordinal))
            {
                continue;
            }

            // Format: "svce"<blob>="DiscordConduit:somekey"
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0)
            {
                continue;
            }

            var value = trimmed[(eqIndex + 1)..].Trim().Trim('"');
            if (value.StartsWith(fullPrefix, StringComparison.Ordinal))
            {
                keys.Add(value[KeyPrefix.Length..]);
            }
        }

        return keys;
    }

    private static async Task<ProcessResult> RunSecurityAsync(string command, string args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "security",
            Arguments = $"{command} {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();

        var stdOut = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stdErr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
