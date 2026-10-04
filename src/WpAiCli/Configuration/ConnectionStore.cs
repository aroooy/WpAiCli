using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpAiCli.Configuration;

public sealed class ConnectionStore
{
    private const string LegacyFileName = "connections.json";
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
        var path = WpAiCliPaths.ConnectionsFilePath;
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
                    try
                    {
                        File.Delete(legacyPath); // Remove old copy so it doesn't linger
                    }
                    catch
                    {
                        // Ignore delete errors if in read-only location
                    }
                    return legacyStore;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to migrate legacy connections from '{legacyPath}': {ex.Message}");
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
        var path = WpAiCliPaths.ConnectionsFilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            WpAiCliPaths.EnsureDirectoryExists(directory, securePermissions: true);
        }

        var model = new ConnectionStoreModel
        {
            LastUsedConnection = LastUsedConnection,
            ActiveConnection = ActiveConnection,
            Profiles = Profiles
        };

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, model, SerializerOptions);

        WpAiCliPaths.EnsureSecureFilePermissions(path);
    }

    public ConnectionProfile? GetActiveProfile()
    {
        if (string.IsNullOrWhiteSpace(ActiveConnection))
        {
            return null;
        }
        return Profiles.FirstOrDefault(p => string.Equals(p.Name, ActiveConnection, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetLegacyStorePath()
        => Path.Combine(AppContext.BaseDirectory, LegacyFileName);

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
