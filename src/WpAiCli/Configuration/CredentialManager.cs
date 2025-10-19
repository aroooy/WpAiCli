using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WpAiCli.Configuration;

internal static class CredentialManager
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public static void Save(string targetName, string secret)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new ArgumentException("Target name is required.", nameof(targetName));
        }

        secret ??= string.Empty;
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

    public static string? ReadSecret(string targetName)
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

    public static void Delete(string targetName)
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
