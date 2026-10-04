using WpAiCli.Configuration;
using Xunit;

namespace WpAiCli.Tests;

public class WpAiCliPathsTests : IDisposable
{
    private readonly string _tempTestDir;

    public WpAiCliPathsTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "wpai_paths_tests_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempTestDir))
        {
            try { Directory.Delete(_tempTestDir, true); } catch { }
        }
    }

    [Fact]
    public void Paths_AreUnderWpAiCliFolder()
    {
        Assert.EndsWith(".wpaicli", WpAiCliPaths.ConfigDirectory);
        Assert.EndsWith("connections.json", WpAiCliPaths.ConnectionsFilePath);
        Assert.EndsWith("credentials.json", WpAiCliPaths.CredentialsFilePath);
        Assert.EndsWith("logs", WpAiCliPaths.LogsDirectory);

        Assert.Equal(
            Path.Combine(WpAiCliPaths.ConfigDirectory, "connections.json"),
            WpAiCliPaths.ConnectionsFilePath);
        Assert.Equal(
            Path.Combine(WpAiCliPaths.ConfigDirectory, "credentials.json"),
            WpAiCliPaths.CredentialsFilePath);
        Assert.Equal(
            Path.Combine(WpAiCliPaths.ConfigDirectory, "logs"),
            WpAiCliPaths.LogsDirectory);
    }

    [Fact]
    public void EnsureDirectoryExists_CreatesDirectoryWithSecurePermissions()
    {
        var targetDir = Path.Combine(_tempTestDir, "secure_dir");
        Assert.False(Directory.Exists(targetDir));

        var created = WpAiCliPaths.EnsureDirectoryExists(targetDir);
        Assert.True(Directory.Exists(created));

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(targetDir);
            // Must have UserRead, UserWrite, UserExecute (0700)
            Assert.True((mode & UnixFileMode.UserRead) != 0);
            Assert.True((mode & UnixFileMode.UserWrite) != 0);
            Assert.True((mode & UnixFileMode.UserExecute) != 0);

            // Group and Other should have no permissions
            Assert.True((mode & UnixFileMode.GroupRead) == 0);
            Assert.True((mode & UnixFileMode.OtherRead) == 0);
        }
    }

    [Fact]
    public void EnsureSecureFilePermissions_SetsUserReadWriteOnly()
    {
        Directory.CreateDirectory(_tempTestDir);
        var targetFile = Path.Combine(_tempTestDir, "secret.json");
        File.WriteAllText(targetFile, "{\"secret\":\"token\"}");

        WpAiCliPaths.EnsureSecureFilePermissions(targetFile);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(targetFile);
            // 0600: UserRead | UserWrite
            Assert.True((mode & UnixFileMode.UserRead) != 0);
            Assert.True((mode & UnixFileMode.UserWrite) != 0);
            Assert.True((mode & UnixFileMode.GroupRead) == 0);
            Assert.True((mode & UnixFileMode.OtherRead) == 0);
        }
    }
}
