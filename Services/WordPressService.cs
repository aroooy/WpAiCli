using WpAiCli.Configuration;
using WpAiCli.WordPress;
using WpAiCli.WordPress.Models;

namespace WpAiCli.Services;

public sealed class WordPressService : IDisposable
{
    private readonly WordPressSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly WordPressApiClient _apiClient;

    public WordPressService(WordPressSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = CreateHttpClient();
        _apiClient = new WordPressApiClient(_httpClient, settings.BaseUrl, settings.BearerToken);
    }

    public Task<IReadOnlyList<WordPressPostDetail>> ListPostsAsync(string? status, int? perPage, int? page, CancellationToken cancellationToken)
        => _apiClient.GetPostsAsync(NormalizeEmpty(status), perPage, page, cancellationToken);

    public Task<WordPressPostDetail> GetPostAsync(int id, CancellationToken cancellationToken)
        => _apiClient.GetPostAsync(id, cancellationToken);

    public Task<IReadOnlyList<WordPressRevision>> GetPostRevisionsAsync(int id, CancellationToken cancellationToken)
        => _apiClient.GetPostRevisionsAsync(id, cancellationToken);

    public Task<WordPressRevision> GetPostRevisionAsync(int id, int revisionId, CancellationToken cancellationToken)
        => _apiClient.GetPostRevisionAsync(id, revisionId, cancellationToken);

    public Task<WordPressPostDetail> CreatePostAsync(WordPressCreatePostRequest request, CancellationToken cancellationToken)
        => _apiClient.CreatePostAsync(request, cancellationToken);

    public Task<WordPressPostDetail> UpdatePostAsync(int id, WordPressUpdatePostRequest request, CancellationToken cancellationToken)
        => _apiClient.UpdatePostAsync(id, request, cancellationToken);

    public Task<WordPressDeleteResponse> DeletePostAsync(int id, bool force, CancellationToken cancellationToken)
        => _apiClient.DeletePostAsync(id, force, cancellationToken);

    public Task<IReadOnlyList<WordPressCategory>> ListCategoriesAsync(CancellationToken cancellationToken)
        => _apiClient.GetCategoriesAsync(cancellationToken);

    public Task<WordPressCategory> GetCategoryAsync(int id, CancellationToken cancellationToken)
        => _apiClient.GetCategoryAsync(id, cancellationToken);

    public Task<WordPressDeleteResponse> DeleteCategoryAsync(int id, bool force, CancellationToken cancellationToken)
        => _apiClient.DeleteCategoryAsync(id, force, cancellationToken);

    public Task<IReadOnlyList<WordPressTag>> ListTagsAsync(CancellationToken cancellationToken)
        => _apiClient.GetTagsAsync(cancellationToken);

    public Task<WordPressTag> CreateTagAsync(WordPressCreateTagRequest request, CancellationToken cancellationToken)
        => _apiClient.CreateTagAsync(request, cancellationToken);

    public Task<WordPressTag> GetTagAsync(int id, CancellationToken cancellationToken)
        => _apiClient.GetTagAsync(id, cancellationToken);

    public Task<WordPressDeleteResponse> DeleteTagAsync(int id, bool force, CancellationToken cancellationToken)
        => _apiClient.DeleteTagAsync(id, force, cancellationToken);

    public Task<IReadOnlyList<WordPressMediaItem>> ListMediaAsync(int? perPage, int? page, CancellationToken cancellationToken)
        => _apiClient.GetMediaAsync(perPage, page, cancellationToken);

    public Task<WordPressMediaItem> UploadMediaAsync(string filePath, string? title, string? description, CancellationToken cancellationToken)
        => _apiClient.UploadMediaAsync(filePath, title, description, cancellationToken);

    private HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static string? NormalizeEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
