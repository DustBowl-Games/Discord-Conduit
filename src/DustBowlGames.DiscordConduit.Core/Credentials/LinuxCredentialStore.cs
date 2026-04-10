using System.Diagnostics;
using System.Runtime.Versioning;

namespace DustBowlGames.DiscordConduit.Core.Credentials;

/// <summary>
/// Credential store backed by libsecret via the <c>secret-tool</c> CLI.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxCredentialStore : ICredentialStore
{
    private const string KeyPrefix = "DiscordConduit:";
    private const string ServiceName = "DiscordConduit";

    /// <inheritdoc />
    public async Task SaveAsync(string key, string secret)
    {
        var targetKey = KeyPrefix + key;
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "secret-tool",
            Arguments = $"store --label=\"{targetKey}\" service {ServiceName} account \"{targetKey}\"",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();
        await process.StandardInput.WriteAsync(secret).ConfigureAwait(false);
        process.StandardInput.Close();
        var stdErr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"secret-tool store failed (exit {process.ExitCode}): {stdErr}");
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key)
    {
        var targetKey = KeyPrefix + key;
        var result = await RunSecretToolAsync(
            $"lookup service {ServiceName} account \"{targetKey}\"").ConfigureAwait(false);

        if (result.ExitCode != 0 || string.IsNullOrEmpty(result.StdOut))
        {
            return null;
        }

        return result.StdOut.Trim();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key)
    {
        var targetKey = KeyPrefix + key;
        await RunSecretToolAsync(
            $"clear service {ServiceName} account \"{targetKey}\"").ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListKeysAsync(string prefix)
    {
        var result = await RunSecretToolAsync(
            $"search --all service {ServiceName}").ConfigureAwait(false);

        var keys = new List<string>();

        if (result.ExitCode != 0)
        {
            return keys;
        }

        var fullPrefix = KeyPrefix + prefix;

        // Parse output looking for "attribute.account = DiscordConduit:..." lines.
        foreach (var line in result.StdOut.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("attribute.account", StringComparison.Ordinal))
            {
                continue;
            }

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0)
            {
                continue;
            }

            var value = trimmed[(eqIndex + 1)..].Trim();
            if (value.StartsWith(fullPrefix, StringComparison.Ordinal))
            {
                keys.Add(value[KeyPrefix.Length..]);
            }
        }

        return keys;
    }

    private static async Task<ProcessResult> RunSecretToolAsync(string args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "secret-tool",
            Arguments = args,
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
