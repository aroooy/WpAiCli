using System.Text.Json;
using System.Text.Json.Serialization;

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

    public static ConnectionStore Load()
    {
        var path = GetStorePath();
        if (!File.Exists(path))
        {
            return new ConnectionStore();
        }

        using var stream = File.OpenRead(path);
        var model = JsonSerializer.Deserialize<ConnectionStoreModel>(stream, SerializerOptions) ?? new ConnectionStoreModel();

        var store = new ConnectionStore
        {
            LastUsedConnection = model.LastUsedConnection
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
        }

        var model = new ConnectionStoreModel
        {
            LastUsedConnection = LastUsedConnection,
            Profiles = Profiles
        };

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, model, SerializerOptions);
    }

    private static string GetStorePath()
        => Path.Combine(AppContext.BaseDirectory, FileName);

    private sealed class ConnectionStoreModel
    {
        [JsonPropertyName("lastUsedConnection")]
        public string? LastUsedConnection { get; set; }

        [JsonPropertyName("profiles")]
        public List<ConnectionProfile> Profiles { get; set; } = new();
    }
}
