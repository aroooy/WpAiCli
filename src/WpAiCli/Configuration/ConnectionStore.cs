using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace WpAiCli.Configuration;

public sealed class ConnectionStore
{
    private const string FileName = "connections.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public List<ConnectionProfile> Profiles { get; } = new();

    public string? LastUsedConnection { get; set; }
    
    public string? ActiveConnection { get; set; }

    public static ConnectionStore Load()
    {
        var path = GetStorePath();
        if (!File.Exists(path))
        {
            // Migration: Check legacy path in AppContext.BaseDirectory
            var legacyPath = GetLegacyStorePath();
            if (File.Exists(legacyPath))
            {
                try
                {
                    var legacyStore = LoadFromPath(legacyPath);
                    legacyStore.Save(); // Migrate to new path (~/.wpaicli/connections.json)
                    return legacyStore;
                }
                catch
                {
                    // Fall back to empty store if legacy read fails
                }
            }

            return new ConnectionStore();
        }

        return LoadFromPath(path);
    }

    private static ConnectionStore LoadFromPath(string path)
    {
        using var stream = File.OpenRead(path);
        var model = JsonSerializer.Deserialize<ConnectionStoreModel>(stream, SerializerOptions) ?? new ConnectionStoreModel();

        var store = new ConnectionStore
        {
            LastUsedConnection = model.LastUsedConnection,
            ActiveConnection = model.ActiveConnection
        };

        if (model.Profiles is { Count: > 0 })
        {
            store.Profiles.AddRange(model.Profiles);
        }

        return store;
    }

    public void Save()
    {
        var path = GetStorePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch
                {
                    // Ignore if filesystem does not support POSIX modes
                }
            }
        }

        var model = new ConnectionStoreModel
        {
            LastUsedConnection = LastUsedConnection,
            ActiveConnection = ActiveConnection,
            Profiles = Profiles
        };

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, model, SerializerOptions);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // Ignore if filesystem does not support POSIX modes
            }
        }
    }

    public ConnectionProfile? GetActiveProfile()
    {
        if (string.IsNullOrWhiteSpace(ActiveConnection))
        {
            return null;
        }
        return Profiles.FirstOrDefault(p => string.Equals(p.Name, ActiveConnection, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetConfigDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".wpaicli");
    }

    private static string GetStorePath()
        => Path.Combine(GetConfigDirectory(), FileName);

    private static string GetLegacyStorePath()
        => Path.Combine(AppContext.BaseDirectory, FileName);

    private sealed class ConnectionStoreModel
    {
        [JsonPropertyName("lastUsedConnection")]
        public string? LastUsedConnection { get; set; }
        
        [JsonPropertyName("activeConnection")]
        public string? ActiveConnection { get; set; }

        [JsonPropertyName("profiles")]
        public List<ConnectionProfile> Profiles { get; set; } = new();
    }
}
