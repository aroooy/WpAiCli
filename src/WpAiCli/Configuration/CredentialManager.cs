using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WpAiCli.Configuration;

// NOTE:
// Cross-platform credential storage helper.
// - On Windows, uses the native Credential Manager (Advapi32.dll).
// - On macOS/Linux, falls back to a per-user JSON file under ~/.wpaicli/credentials.json.
// This keeps behavior consistent across OSes without external dependencies.

internal static class CredentialManager
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // Windows-specific constants
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    // Non-Windows credential file path
    // (Readable and simple; contents are not encrypted—suitable for dev machines.)
    private static readonly string CredentialFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".wpaicli",
        "credentials.json");

    public static void Save(string targetName, string secret)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new ArgumentException("Target name is required.", nameof(targetName));
        }
        secret ??= string.Empty;

        if (IsWindows)
        {
            SaveForWindows(targetName, secret);
        }
        else
        {
            SaveForNonWindows(targetName, secret);
        }
    }

    public static string? ReadSecret(string targetName)
    {
        if (IsWindows)
        {
            return ReadSecretForWindows(targetName);
        }
        else
        {
            return ReadSecretForNonWindows(targetName);
        }
    }

    public static void Delete(string targetName)
    {
        if (IsWindows)
        {
            DeleteForWindows(targetName);
        }
        else
        {
            DeleteForNonWindows(targetName);
        }
    }

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

    private static Dictionary<string, string> ReadCredentialFile()
    {
        if (!File.Exists(CredentialFilePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        var json = File.ReadAllText(CredentialFilePath);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteCredentialFile(Dictionary<string, string> credentials)
    {
        var directory = Path.GetDirectoryName(CredentialFilePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CredentialFilePath, json);
    }

    private static void SaveForNonWindows(string targetName, string secret)
    {
        var credentials = ReadCredentialFile();
        credentials[targetName] = secret;
        WriteCredentialFile(credentials);
    }

    private static string? ReadSecretForNonWindows(string targetName)
    {
        var credentials = ReadCredentialFile();
        return credentials.TryGetValue(targetName, out var secret) ? secret : null;
    }

    private static void DeleteForNonWindows(string targetName)
    {
        var credentials = ReadCredentialFile();
        if (credentials.Remove(targetName))
        {
            WriteCredentialFile(credentials);
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
