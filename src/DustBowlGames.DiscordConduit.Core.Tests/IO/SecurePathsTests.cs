using DustBowlGames.DiscordConduit.Core.IO;

namespace DustBowlGames.DiscordConduit.Core.Tests.IO;

public class SecurePathsTests
{
    [Fact]
    public void CreateOwnerOnlyDirectory_CreatesDirectoryAndReturnsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "conduit-secpaths-" + Guid.NewGuid().ToString("N"));
        try
        {
            var returned = SecurePaths.CreateOwnerOnlyDirectory(path);

            Assert.Equal(path, returned);
            Assert.True(Directory.Exists(path));

            // On Unix the directory should be restricted to the owner (0700). On Windows
            // SetUnixFileMode is a no-op and the mode is not asserted.
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(path);
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    mode);
            }
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void CreateOwnerOnlyDirectory_IsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), "conduit-secpaths-" + Guid.NewGuid().ToString("N"));
        try
        {
            SecurePaths.CreateOwnerOnlyDirectory(path);
            // Second call on an existing directory must not throw.
            var returned = SecurePaths.CreateOwnerOnlyDirectory(path);
            Assert.Equal(path, returned);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
