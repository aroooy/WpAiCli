using System.Text.Json.Serialization;

namespace WpAiCli.Configuration;

public sealed class ConnectionProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("credentialKey")]
    public string CredentialKey { get; set; } = string.Empty;
}
