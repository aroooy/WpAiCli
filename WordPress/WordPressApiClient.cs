using System;
using System.Collections.Generic;
using System.IO;
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

    // Fields constants for reuse
    private const string PostSummaryFields = "id,date,date_gmt,guid,modified,modified_gmt,slug,status,type,link,title";
    private const string PostDetailFields = "id,date,date_gmt,guid.raw,modified,modified_gmt,password,slug,status,type,link,title.raw,content.raw,excerpt,author,featured_media,comment_status,ping_status,sticky,template,format,categories,tags,permalink_template,generated_slug,class_list";
    private const string RevisionSummaryFields = "author,date_gmt,id,modified_gmt,parent,title";
    private const string RevisionDetailFields = "author,date_gmt,id,modified_gmt,parent,title,content";
    private const string CategoryFields = "id,count,description,link,name,slug,taxonomy,parent";
    private const string TagFields = "id,name,slug,description,count";
    private const string MediaFields = "id,date,title,media_type,mime_type,source_url";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
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

    public async Task<IReadOnlyList<WordPressPostDetail>> GetPostsAsync(string? status, int? perPage, int? page, CancellationToken cancellationToken)
    {
        var url = BuildUrl("/posts");
        url = AppendQuery(url, "context", "edit");
        url = AppendQuery(url, "_fields", PostDetailFields);

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

        return await SendAsync<List<WordPressPostDetail>>(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
    }

    public Task<WordPressPostDetail> GetPostAsync(int id, CancellationToken cancellationToken)
    {
        var url = BuildUrl($"/posts/{id}");
        url = AppendQuery(url, "context", "edit");
        url = AppendQuery(url, "_fields", PostDetailFields);
        return SendAsync<WordPressPostDetail>(HttpMethod.Get, url, cancellationToken);
    }

    public async Task<IReadOnlyList<WordPressRevision>> GetPostRevisionsAsync(int id, CancellationToken cancellationToken)
    {
        var url = BuildUrl($"/posts/{id}/revisions");
        url = AppendQuery(url, "per_page", "5");
        url = AppendQuery(url, "context", "edit");
        url = AppendQuery(url, "_fields", RevisionSummaryFields);
        return await SendAsync<List<WordPressRevision>>(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
    }

    public Task<WordPressRevision> GetPostRevisionAsync(int id, int revisionId, CancellationToken cancellationToken)
    {
        var url = BuildUrl($"/posts/{id}/revisions/{revisionId}");
        url = AppendQuery(url, "context", "edit");
        url = AppendQuery(url, "_fields", RevisionDetailFields);
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

        var url = BuildUrl("/posts");
        return SendAsync<WordPressPostDetail>(HttpMethod.Post, url, cancellationToken, request);
    }

    public Task<WordPressPostDetail> UpdatePostAsync(int id, WordPressUpdatePostRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAuthenticated(nameof(UpdatePostAsync));

        var url = BuildUrl($"/posts/{id}");
        var method = new HttpMethod("PATCH");
        return SendAsync<WordPressPostDetail>(method, url, cancellationToken, request);
    }

    public async Task<WordPressDeleteResponse> DeletePostAsync(int id, bool force, CancellationToken cancellationToken)
    {
        EnsureAuthenticated(nameof(DeletePostAsync));

        var url = BuildUrl($"/posts/{id}");
        if (force)
        {
            url = AppendQuery(url, "force", "true");
        }

        await SendAsync<JsonDocument>(HttpMethod.Delete, url, cancellationToken).ConfigureAwait(false);
        return new WordPressDeleteResponse { Deleted = true };
    }

    public async Task<IReadOnlyList<WordPressCategory>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var url = BuildUrl("/categories");
        url = AppendQuery(url, "_fields", CategoryFields);
        url = AppendQuery(url, "per_page", "100");
        return await SendAsync<List<WordPressCategory>>(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
    }

    public Task<WordPressCategory> GetCategoryAsync(int id, CancellationToken cancellationToken)
    {
        var url = BuildUrl($"/categories/{id}");
        url = AppendQuery(url, "_fields", CategoryFields);
        return SendAsync<WordPressCategory>(HttpMethod.Get, url, cancellationToken);
    }

    public async Task<WordPressDeleteResponse> DeleteCategoryAsync(int id, bool force, CancellationToken cancellationToken)
    {
        EnsureAuthenticated(nameof(DeleteCategoryAsync));

        var url = BuildUrl($"/categories/{id}");
        if (force)
        {
            url = AppendQuery(url, "force", "true");
        }

        await SendAsync<JsonDocument>(HttpMethod.Delete, url, cancellationToken).ConfigureAwait(false);
        return new WordPressDeleteResponse { Deleted = true };
    }

    public async Task<IReadOnlyList<WordPressTag>> GetTagsAsync(CancellationToken cancellationToken)
    {
        var url = BuildUrl("/tags");
        url = AppendQuery(url, "_fields", TagFields);
        url = AppendQuery(url, "per_page", "100");
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

        var url = BuildUrl("/tags");
        return SendAsync<WordPressTag>(HttpMethod.Post, url, cancellationToken, request);
    }

    public Task<WordPressTag> GetTagAsync(int id, CancellationToken cancellationToken)
    {
        var url = BuildUrl($"/tags/{id}");
        url = AppendQuery(url, "_fields", TagFields);
        return SendAsync<WordPressTag>(HttpMethod.Get, url, cancellationToken);
    }

    public async Task<WordPressDeleteResponse> DeleteTagAsync(int id, bool force, CancellationToken cancellationToken)
    {
        EnsureAuthenticated(nameof(DeleteTagAsync));

        var url = BuildUrl($"/tags/{id}");
        if (force)
        {
            url = AppendQuery(url, "force", "true");
        }

        await SendAsync<JsonDocument>(HttpMethod.Delete, url, cancellationToken).ConfigureAwait(false);
        return new WordPressDeleteResponse { Deleted = true };
    }

    public async Task<IReadOnlyList<WordPressMediaItem>> GetMediaAsync(int? perPage, int? page, CancellationToken cancellationToken)
    {
        var url = BuildUrl("/media");
        url = AppendQuery(url, "_fields", MediaFields);
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

    public async Task<WordPressMediaItem> UploadMediaAsync(string filePath, string? title, string? description, CancellationToken cancellationToken)
    {
        EnsureAuthenticated(nameof(UploadMediaAsync));

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The specified file for upload was not found.", filePath);
        }

        var url = BuildUrl("/media");
        using var formData = new MultipartFormDataContent();

        var fileStream = File.OpenRead(filePath);
        var streamContent = new StreamContent(fileStream);
        var fileName = Path.GetFileName(filePath);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(MimeMapping.MimeUtility.GetMimeMapping(fileName));
        formData.Add(streamContent, "file", fileName);

        if (!string.IsNullOrWhiteSpace(title))
        {
            formData.Add(new StringContent(title), "title");
        }
        if (!string.IsNullOrWhiteSpace(description))
        {
            formData.Add(new StringContent(description), "description");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }
        request.Content = formData;

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WordPressApiException(response.StatusCode, payload);
        }

        var result = JsonSerializer.Deserialize<WordPressMediaItem>(payload, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Failed to deserialize WordPress API response for media upload.");
        }

        return result;
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

    private sealed class VoidResult { }

    private static class MimeMapping
    {
        public static class MimeUtility
        {
            private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
            {
                {".png", "image/png"},
                {".jpg", "image/jpeg"},
                {".jpeg", "image/jpeg"},
                {".gif", "image/gif"},
                {".bmp", "image/bmp"},
                {".webp", "image/webp"},
                {".svg", "image/svg+xml"},
                {".txt", "text/plain"},
                {".pdf", "application/pdf"},
                {".zip", "application/zip"},
                {".mp4", "video/mp4"},
                {".mov", "video/quicktime"},
            };

            public static string GetMimeMapping(string fileName)
            {
                var extension = Path.GetExtension(fileName);
                if (extension != null && MimeTypes.TryGetValue(extension, out var mimeType))
                {
                    return mimeType;
                }
                return "application/octet-stream";
            }
        }
    }
}