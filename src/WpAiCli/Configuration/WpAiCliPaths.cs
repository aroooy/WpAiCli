using System;
using System.IO;

namespace WpAiCli.Configuration;

public static class WpAiCliPaths
{
    private const string AppDirName = ".wpaicli";
    private const string ConnectionsFileName = "connections.json";
    private const string CredentialsFileName = "credentials.json";
    private const string LogsDirName = "logs";

    public static string ConfigDirectory
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, AppDirName);
        }
    }

    public static string ConnectionsFilePath => Path.Combine(ConfigDirectory, ConnectionsFileName);

    public static string CredentialsFilePath => Path.Combine(ConfigDirectory, CredentialsFileName);

    public static string LogsDirectory => Path.Combine(ConfigDirectory, LogsDirName);

    public static string EnsureDirectoryExists(string dirPath, bool securePermissions = true)
    {
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

        if (securePermissions && !OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(dirPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch
            {
                // Ignore on filesystems without POSIX permissions
            }
        }

        return dirPath;
    }

    public static void EnsureSecureFilePermissions(string filePath)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            if (File.Exists(filePath))
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Ignore on filesystems without POSIX permissions
        }
    }
}
