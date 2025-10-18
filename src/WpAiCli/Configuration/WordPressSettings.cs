namespace WpAiCli.Configuration;

public sealed class WordPressSettings
{
    public WordPressSettings(string baseUrl, string bearerToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL is required.", nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new ArgumentException("Bearer token is required.", nameof(bearerToken));
        }

        BaseUrl = baseUrl.TrimEnd('/') + "/";
        BearerToken = bearerToken;
    }

    public string BaseUrl { get; }
    public string BearerToken { get; }
}
