using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WpAiCli.WordPress.Models;

namespace WpAiCli.WordPress;

public sealed class WordPressApiClient
{
    private const string DefaultBaseUrl = "https://aroooy.net/?rest_route=/wp/v2";
    private const string PostsListPath = "/posts&context=edit&_fields=id,date,date_gmt,guid,modified,modified_gmt,slug,status,type,link,title";
    private const string PostDetailPath = "/posts/{0}&context=edit&_fields=id,date,date_gmt,guid.raw,modified,modified_gmt,password,slug,status,type,link,title.raw,content.raw,author,featured_media,comment_status,ping_status,sticky,template,format,categories,tags,permalink_template,generated_slug,class_list";
    private const string PostCreatePath = "/posts";
    private const string PostRevisionsPath = "/posts/{0}/revisions&per_page=5&context=edit";
    private const string PostRevisionDetailPath = "/posts/{0}/revisions/{1}&context=edit";
    private const string CategoriesPath = "/categories&_fields=id,count,description,link,name,slug,taxonomy,parent&per_page=100";
    private const string CategoryDetailPath = "/categories/{0}";
    private const string TagsPath = "/tags&_fields=id,name,slug,description,count&per_page=100";
    private const string TagCreatePath = "/tags";
    private const string TagDetailPath = "/tags/{0}";
    private const string MediaPath = "/media&_fields=id,date,title,media_type,mime_type,source_url";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string? _bearerToken;

    public WordPressApiClient(HttpClient httpClient, string? baseUrl, string? bearerToken)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl);
        _bearerToken = bearerToken;
    }

    public async Task<IReadOnlyList<WordPressPostSummary>> GetPostsAsync(string? status, int? perPage, int? page, CancellationToken cancellationToken)
    {
        var url = BuildUrl(PostsListPath);
        if (!string.IsNullOrWhiteSpace(status))
        {
            url = AppendQuery(url, "status", status);
        }
        if (perPage.HasValue)
        {
            url = AppendQuery(url, "per_page", perPage.Value.ToString());
        }
        if (page.HasValue)
        {
            url = AppendQuery(url, "page", page.Value.ToString());
        }

        return await SendAsync<List<WordPressPostSummary>>(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
    }

    public Task<WordPressPostDetail> GetPostAsync(int id, CancellationToken cancellationToken)
    {
        var path = string.Format(PostDetailPath, id);
        var url = BuildUrl(path);
        return SendAsync<WordPressPostDetail>(HttpMethod.Get, url, cancellationToken);
    }

    public async Task<IReadOnlyList<WordPressRevision>> GetPostRevisionsAsync(int id, CancellationToken cancellationToken)
    {
        var path = string.Format(PostRevisionsPath, id);
        var url = BuildUrl(path);
        return await SendAsync<List<WordPressRevision>>(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
    }

    public Task<WordPressRevision> GetPostRevisionAsync(int id, int revisionId, CancellationToken cancellationToken)
    {
        var path = string.Format(PostRevisionDetailPath, id, revisionId);
        var url = BuildUrl(path);
        return SendAsync<WordPressRevision>(HttpMethod.Get, url, cancellationToken);
    }

    public Task<WordPressPostDetail> CreatePostAsync(WordPressCreatePostRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAuthenticated(nameof(CreatePostAsync));

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.Status))
        {
            throw new ArgumentException("Title, content, and status are required to create a post.", nameof(request));
        }

        var url = BuildUrl(PostCreatePath);
        return SendAsync<WordPressPostDetail>(HttpMethod.Post, url, cancellationToken, request);
    }

    public Task<WordPressPostDetail> UpdatePostAsync(int id, WordPressUpdatePostRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAuthenticated(nameof(UpdatePostAsync));

        var path = string.Format(PostDetailPath, id);
        var url = BuildUrl(path);
        var method = new HttpMethod("PATCH");
        return SendAsync<WordPressPostDetail>(method, url, cancellationToken, request);
    }

    public Task<WordPressDeleteResponse> DeletePostAsync(int id, bool force, CancellationToken cancellationToken)
    {
        EnsureAuthenticated(nameof(DeletePostAsync));

        var path = string.Format(PostDetailPath, id);
        var url = BuildUrl(path);
        if (force)
        {
            url = AppendQuery(url, "force", "true");
        }

        return SendAsync<WordPressDeleteResponse>(HttpMethod.Delete, url, cancellationToken);
    }

    public async Task<IReadOnlyList<WordPressCategory>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var url = BuildUrl(CategoriesPath);
        return await SendAsync<List<WordPressCategory>>(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
    }

    public Task<WordPressCategory> GetCategoryAsync(int id, CancellationToken cancellationToken)
    {
        var path = string.Format(CategoryDetailPath, id);
        var url = BuildUrl(path);
        return SendAsync<WordPressCategory>(HttpMethod.Get, url, cancellationToken);
    }

    public Task<WordPressDeleteResponse> DeleteCategoryAsync(int id, bool force, CancellationToken cancellationToken)
    {
        EnsureAuthenticated(nameof(DeleteCategoryAsync));

        var path = string.Format(CategoryDetailPath, id);
        var url = BuildUrl(path);
        if (force)
        {
            url = AppendQuery(url, "force", "true");
        }

        return SendAsync<WordPressDeleteResponse>(HttpMethod.Delete, url, cancellationToken);
    }

    public async Task<IReadOnlyList<WordPressTag>> GetTagsAsync(CancellationToken cancellationToken)
    {
        var url = BuildUrl(TagsPath);
        return await SendAsync<List<WordPressTag>>(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
    }

    public Task<WordPressTag> CreateTagAsync(WordPressCreateTagRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAuthenticated(nameof(CreateTagAsync));

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tag name is required.", nameof(request));
        }

        var url = BuildUrl(TagCreatePath);
        return SendAsync<WordPressTag>(HttpMethod.Post, url, cancellationToken, request);
    }

    public Task<WordPressTag> GetTagAsync(int id, CancellationToken cancellationToken)
    {
        var path = string.Format(TagDetailPath, id);
        var url = BuildUrl(path);
        return SendAsync<WordPressTag>(HttpMethod.Get, url, cancellationToken);
    }

    public Task<WordPressDeleteResponse> DeleteTagAsync(int id, bool force, CancellationToken cancellationToken)
    {
        EnsureAuthenticated(nameof(DeleteTagAsync));

        var path = string.Format(TagDetailPath, id);
        var url = BuildUrl(path);
        if (force)
        {
            url = AppendQuery(url, "force", "true");
        }

        return SendAsync<WordPressDeleteResponse>(HttpMethod.Delete, url, cancellationToken);
    }

    public async Task<IReadOnlyList<WordPressMediaItem>> GetMediaAsync(int? perPage, int? page, CancellationToken cancellationToken)
    {
        var url = BuildUrl(MediaPath);
        if (perPage.HasValue)
        {
            url = AppendQuery(url, "per_page", perPage.Value.ToString());
        }
        if (page.HasValue)
        {
            url = AppendQuery(url, "page", page.Value.ToString());
        }

        return await SendAsync<List<WordPressMediaItem>>(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string requestUri, CancellationToken cancellationToken, object? body = null)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WordPressApiException(response.StatusCode, payload);
        }

        if (typeof(T) == typeof(VoidResult))
        {
            return default!;
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("WordPress API returned an empty response body.");
        }

        var result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Failed to deserialize WordPress API response.");
        }

        return result;
    }

    private string BuildUrl(string relativePath)
    {
        if (!relativePath.StartsWith('/'))
        {
            relativePath = "/" + relativePath;
        }

        return string.Concat(_baseUrl, relativePath);
    }

    private static string AppendQuery(string url, string key, string value)
    {
        var separator = url.Contains('?') ? '&' : '?';
        var builder = new StringBuilder(url);
        builder.Append(separator);
        builder.Append(Uri.EscapeDataString(key));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(value));
        return builder.ToString();
    }

    private string NormalizeBaseUrl(string baseUrl)
    {
        if (baseUrl.EndsWith('/'))
        {
            baseUrl = baseUrl.TrimEnd('/');
        }
        return baseUrl;
    }

    private void EnsureAuthenticated(string operation)
    {
        if (string.IsNullOrWhiteSpace(_bearerToken))
        {
            throw new InvalidOperationException($"{operation} requires a valid bearer token.");
        }
    }

    private sealed class VoidResult
    {
    }
}
