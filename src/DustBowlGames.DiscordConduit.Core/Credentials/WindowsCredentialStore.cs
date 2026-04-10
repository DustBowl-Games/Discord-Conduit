using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DustBowlGames.DiscordConduit.Core.Credentials;

/// <summary>
/// Credential store backed by Windows Credential Manager via P/Invoke.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const string KeyPrefix = "DiscordConduit:";

    /// <inheritdoc />
    public Task SaveAsync(string key, string secret)
    {
        var targetName = KeyPrefix + key;
        var secretBytes = Encoding.UTF8.GetBytes(secret);

        var cred = new NativeCredential
        {
            Type = CredTypeGeneric,
            TargetName = targetName,
            CredentialBlobSize = (uint)secretBytes.Length,
            Persist = CredPersistLocalMachine,
        };

        var blobPtr = Marshal.AllocHGlobal(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, blobPtr, secretBytes.Length);
            cred.CredentialBlob = blobPtr;

            if (!CredWriteW(ref cred, 0))
            {
                throw new InvalidOperationException(
                    $"CredWriteW failed with error code {Marshal.GetLastWin32Error()}.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> GetAsync(string key)
    {
        var targetName = KeyPrefix + key;

        if (!CredReadW(targetName, CredTypeGeneric, 0, out var credPtr))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(null);
            }

            var secretBytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, secretBytes, 0, (int)cred.CredentialBlobSize);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(secretBytes));
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key)
    {
        var targetName = KeyPrefix + key;
        CredDeleteW(targetName, CredTypeGeneric, 0);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListKeysAsync(string prefix)
    {
        var filter = KeyPrefix + prefix + "*";
        var results = new List<string>();

        if (!CredEnumerateW(filter, 0, out var count, out var credArrayPtr))
        {
            return Task.FromResult<IReadOnlyList<string>>(results);
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                var credPtr = Marshal.ReadIntPtr(credArrayPtr, i * IntPtr.Size);
                var cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
                if (cred.TargetName is not null && cred.TargetName.StartsWith(KeyPrefix, StringComparison.Ordinal))
                {
                    results.Add(cred.TargetName[KeyPrefix.Length..]);
                }
            }
        }
        finally
        {
            CredFree(credArrayPtr);
        }

        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWriteW(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredEnumerateW(string? filter, uint flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
