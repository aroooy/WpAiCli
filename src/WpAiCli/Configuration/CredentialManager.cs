using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WpAiCli.Configuration;

// NOTE:
// Cross-platform credential storage helper.
// - On Windows, uses native Credential Manager (Advapi32.dll).
// - On macOS, uses native Keychain Services via security CLI (-i interactive stdin shell, preventing command-line arg exposure).
// - On Linux, uses secret-tool (libsecret) via stdin.
// - Falls back to a permission-restricted file (chmod 600) under ~/.wpaicli/credentials.json if native store is unavailable.
// Automatically migrates existing credentials from fallback file into the native OS store.

internal static class CredentialManager
{
    private const string ServiceName = "WpAiCli";
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    // Windows-specific constants
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public static void Save(string targetName, string secret)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new ArgumentException("Target name is required.", nameof(targetName));
        }
        secret ??= string.Empty;

        if (IsWindows)
        {
            DeleteForWindows(targetName); // Overwrite by deleting first
            SaveForWindows(targetName, secret);
            return;
        }

        if (IsMacOS)
        {
            if (TrySaveForMacOS(targetName, secret))
            {
                DeleteFromFile(targetName);
                return;
            }
        }
        else if (IsLinux)
        {
            if (TrySaveForLinux(targetName, secret))
            {
                DeleteFromFile(targetName);
                return;
            }
        }

        // Fallback to permission-restricted local file
        SaveToFile(targetName, secret);
    }

    public static string? ReadSecret(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        if (IsWindows)
        {
            return ReadSecretForWindows(targetName);
        }

        if (IsMacOS)
        {
            var secret = ReadSecretForMacOS(targetName);
            if (!string.IsNullOrEmpty(secret))
            {
                return secret;
            }
        }
        else if (IsLinux)
        {
            var secret = ReadSecretForLinux(targetName);
            if (!string.IsNullOrEmpty(secret))
            {
                return secret;
            }
        }

        // Check fallback file (for existing credentials or when native store is unavailable)
        var fallbackSecret = ReadSecretFromFile(targetName);
        if (!string.IsNullOrEmpty(fallbackSecret))
        {
            // Auto-migrate to native OS store if now available
            if (IsMacOS && TrySaveForMacOS(targetName, fallbackSecret))
            {
                DeleteFromFile(targetName);
            }
            else if (IsLinux && TrySaveForLinux(targetName, fallbackSecret))
            {
                DeleteFromFile(targetName);
            }
            else
            {
                // Ensure existing fallback file permissions are restricted to owner only (chmod 600)
                WpAiCliPaths.EnsureSecureFilePermissions(WpAiCliPaths.CredentialsFilePath);
            }
            return fallbackSecret;
        }

        return null;
    }

    public static void Delete(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return;
        }

        if (IsWindows)
        {
            DeleteForWindows(targetName);
            return;
        }

        if (IsMacOS)
        {
            DeleteForMacOS(targetName);
        }
        else if (IsLinux)
        {
            DeleteForLinux(targetName);
        }

        DeleteFromFile(targetName);
    }

    // --- macOS Keychain (security CLI via interactive stdin shell) ---

    private static bool TrySaveForMacOS(string targetName, string secret)
    {
        try
        {
            // Use 'security -i' interactive shell over stdin to avoid exposing secret in process command-line arguments.
            // Using -X <hex> ensures that special characters, quotes, or spaces in secrets do not break syntax.
            var psi = new ProcessStartInfo("security")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-i");

            using var process = Process.Start(psi);
            if (process == null) return false;

            var hexSecret = Convert.ToHexString(Encoding.UTF8.GetBytes(secret));
            var sanitizedTarget = targetName.Replace("\"", "\\\"");
            process.StandardInput.WriteLine($"add-generic-password -a \"{sanitizedTarget}\" -s \"{ServiceName}\" -U -X {hexSecret}");
            process.StandardInput.Close();

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadSecretForMacOS(string targetName)
    {
        try
        {
            var psi = new ProcessStartInfo("security")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("find-generic-password");
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(targetName);
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(ServiceName);
            psi.ArgumentList.Add("-w");

            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.TrimEnd('\r', '\n') : null;
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteForMacOS(string targetName)
    {
        try
        {
            var psi = new ProcessStartInfo("security")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-i");

            using var process = Process.Start(psi);
            if (process == null) return;

            var sanitizedTarget = targetName.Replace("\"", "\\\"");
            process.StandardInput.WriteLine($"delete-generic-password -a \"{sanitizedTarget}\" -s \"{ServiceName}\"");
            process.StandardInput.Close();

            process.WaitForExit();
        }
        catch
        {
            // Ignore error
        }
    }

    // --- Linux secret-tool (via stdin) ---

    private static bool TrySaveForLinux(string targetName, string secret)
    {
        try
        {
            var psi = new ProcessStartInfo("secret-tool")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("store");
            psi.ArgumentList.Add($"--label={ServiceName}: {targetName}");
            psi.ArgumentList.Add("service");
            psi.ArgumentList.Add(ServiceName);
            psi.ArgumentList.Add("account");
            psi.ArgumentList.Add(targetName);

            using var process = Process.Start(psi);
            if (process == null) return false;
            process.StandardInput.WriteLine(secret);
            process.StandardInput.Close();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadSecretForLinux(string targetName)
    {
        try
        {
            var psi = new ProcessStartInfo("secret-tool")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("lookup");
            psi.ArgumentList.Add("service");
            psi.ArgumentList.Add(ServiceName);
            psi.ArgumentList.Add("account");
            psi.ArgumentList.Add(targetName);

            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.TrimEnd('\r', '\n') : null;
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteForLinux(string targetName)
    {
        try
        {
            var psi = new ProcessStartInfo("secret-tool")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("clear");
            psi.ArgumentList.Add("service");
            psi.ArgumentList.Add(ServiceName);
            psi.ArgumentList.Add("account");
            psi.ArgumentList.Add(targetName);

            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        catch
        {
            // Ignore error
        }
    }

    // --- File Fallback with Restricted Permissions (chmod 600) ---

    private static Dictionary<string, string> ReadCredentialFile()
    {
        var filePath = WpAiCliPaths.CredentialsFilePath;
        if (!File.Exists(filePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void WriteCredentialFile(Dictionary<string, string> credentials)
    {
        var filePath = WpAiCliPaths.CredentialsFilePath;
        var directory = Path.GetDirectoryName(filePath);
        if (directory != null)
        {
            WpAiCliPaths.EnsureDirectoryExists(directory, securePermissions: true);
        }
        var json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
        WpAiCliPaths.EnsureSecureFilePermissions(filePath);
    }

    private static void SaveToFile(string targetName, string secret)
    {
        var credentials = ReadCredentialFile();
        credentials[targetName] = secret;
        WriteCredentialFile(credentials);
    }

    private static string? ReadSecretFromFile(string targetName)
    {
        var credentials = ReadCredentialFile();
        return credentials.TryGetValue(targetName, out var secret) ? secret : null;
    }

    private static void DeleteFromFile(string targetName)
    {
        var filePath = WpAiCliPaths.CredentialsFilePath;
        if (!File.Exists(filePath)) return;
        var credentials = ReadCredentialFile();
        if (credentials.Remove(targetName))
        {
            if (credentials.Count == 0)
            {
                try { File.Delete(filePath); } catch { }
            }
            else
            {
                WriteCredentialFile(credentials);
            }
        }
    }

    // --- Windows Credential Manager P/Invoke ---

    private static void SaveForWindows(string targetName, string secret)
    {
        var secretBytes = Encoding.Unicode.GetBytes(secret);
        var credential = new NativeCredential
        {
            Type = CredTypeGeneric,
            TargetName = targetName,
            CredentialBlobSize = (uint)secretBytes.Length,
            Persist = CredPersistLocalMachine,
            AttributeCount = 0,
            UserName = null
        };

        credential.CredentialBlob = Marshal.AllocCoTaskMem(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, credential.CredentialBlob, secretBytes.Length);
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to save credential '{targetName}'.");
            }
        }
        finally
        {
            if (credential.CredentialBlob != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(credential.CredentialBlob);
            }
        }
    }

    private static string? ReadSecretForWindows(string targetName)
    {
        if (!CredRead(targetName, CredTypeGeneric, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) // ERROR_NOT_FOUND
            {
                return null;
            }
            throw new Win32Exception(error, $"Failed to read credential '{targetName}'.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    private static void DeleteForWindows(string targetName)
    {
        if (!CredDelete(targetName, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) // ERROR_NOT_FOUND
            {
                return;
            }
            throw new Win32Exception(error, $"Failed to delete credential '{targetName}'.");
        }
    }

    // P/Invoke declarations for Windows
    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr cred);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string targetName, int type, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
