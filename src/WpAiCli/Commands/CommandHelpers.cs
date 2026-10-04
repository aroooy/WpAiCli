using System;
using System.IO;
using System.Linq;
using WpAiCli.Configuration;
using WpAiCli.Parsing;

namespace WpAiCli.Commands;

public static class CommandHelpers
{
    public static (ConnectionStore Store, ConnectionProfile Profile, string credential) ResolveConnection()
    {
        var store = ConnectionStore.Load();
        if (store.Profiles.Count == 0)
        {
            throw new InvalidOperationException("No connections registered. Use `wpai connections add` first.");
        }

        ConnectionProfile? profile = null;

        if (!string.IsNullOrWhiteSpace(store.ActiveConnection))
        {
            profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, store.ActiveConnection, StringComparison.OrdinalIgnoreCase));
        }

        if (profile is null)
        {
            throw new InvalidOperationException("No active connection set. Please set one using `wpai connections active`.");
        }

        var credential = CredentialManager.ReadSecret(profile.CredentialKey);
        if (string.IsNullOrWhiteSpace(credential))
        {
            throw new InvalidOperationException($"Credential for connection '{profile.Name}' is missing. Re-add the connection.");
        }

        return (store, profile, credential);
    }

    public static void UpdateLastUsedConnection(ConnectionStore store, string connectionName)
    {
        if (!string.Equals(store.LastUsedConnection, connectionName, StringComparison.OrdinalIgnoreCase))
        {
            store.LastUsedConnection = connectionName;
            store.Save();
        }
    }

    public static int? ResolveId(ParsedOptions parsed, string? defaultValue = null)
    {
        if (parsed.Positionals.Count > 0 && int.TryParse(parsed.Positionals[0], out var positionalId))
        {
            return positionalId;
        }

        if (!string.IsNullOrWhiteSpace(defaultValue) && int.TryParse(defaultValue, out var fallback))
        {
            return fallback;
        }

        return parsed.GetInt("id");
    }

    public static FileInfo? ToFileInfo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new FileInfo(path);
    }

    public static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
        return password.ToString();
    }
}
