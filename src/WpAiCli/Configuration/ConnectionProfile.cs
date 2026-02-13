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

    [JsonPropertyName("authMethod")]
    public string AuthMethod { get; set; } = "ApplicationPassword";

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    public string? CachePath { get; set; }

    public int? SyncItemsLimit { get; set; } = 30;

    public string? MarkdownConversion { get; set; }
}
