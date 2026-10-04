using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using Markdig;
using System.Threading.Tasks;
using WpAiCli.WordPress;
using WpAiCli.WordPress.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using WpAiCli.Configuration;

namespace WpAiCli.Services;

// NOTE:
// TransferReport aggregates side effects from transfer operations (pull, push).
// It is intended for user-friendly reporting rather than strict programmatic consumption.

public class TransferReport
{
    public List<int> PushedToServer { get; } = new();
    public List<int> PulledFromServer { get; } = new();
    public List<int> DeletedFromLocal { get; } = new();
    public List<int> ConflictDetected { get; } = new();
    public List<int> NewlyCached { get; } = new();
    public List<int> LocalEditsKept { get; } = new();
    public List<string> PushedTaxonomies { get; } = new();
    public List<string> PulledTaxonomies { get; } = new();
    public List<(int PostId, string ErrorMessage)> LocalValidationErrors { get; } = new();
    public List<string> MovedPosts { get; } = new();

    // Media Transfer Properties
    public List<int> PushedMediaToServer { get; } = new();
    public List<int> PulledMediaFromServer { get; } = new();
    public List<int> NewlyCachedMedia { get; } = new();
    public List<int> DeletedMediaFromLocal { get; } = new();
    public List<int> MediaConflicts { get; } = new();
}

public class WorkspaceService
{
    private readonly WordPressService _wpService;
    private readonly CacheService _cacheService;
    
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve)
        .Build();

    // Coordinates transfer operations between the local workspace cache and WordPress remote.
    // Posts, taxonomies, and media are handled with pull/push/compare logic.
    public WorkspaceService(WordPressService wpService, CacheService cacheService)
    {
        _wpService = wpService;
        _cacheService = cacheService;
    }

    // Push a single post using local cache (content.md + editable.yaml)
    public async Task<(WordPressPostDetail Post, PostCacheResult CacheResult)> PushPostAsync(int id, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        var localPost = _cacheService.ReadLocalPost(id)
            ?? throw new InvalidOperationException($"Could not read local post data for {id}. Cannot push local changes.");

        var request = new WordPressUpdatePostRequest();
        var localEditableMeta = localPost.Metadata;

        // Initialize Meta from local file, then add/overwrite internal fields
        var metaForRequest = localEditableMeta.Meta ?? new Dictionary<string, object?>();

        // Handle content based on edit mode
        var editMode = localEditableMeta.EditMode ?? "html";
        var conversion = profile.MarkdownConversion ?? "client";

        if (editMode == "markdown")
        {
            metaForRequest["_md_source"] = localPost.Content;
            request.Content = conversion == "client" ? Markdown.ToHtml(localPost.Content) : localPost.Content;
        }
        else // html mode
        {
            request.Content = localPost.Content;
        }

        request.Meta = metaForRequest;

        // Apply all editable metadata fields
        request.Title = localEditableMeta.Title;
        request.Slug = localEditableMeta.Slug;
        request.Status = localEditableMeta.Status;
        if (DateTime.TryParse(localEditableMeta.Date, out var localDate))
        {
            request.Date = localDate;
        }
        request.Excerpt = localEditableMeta.Excerpt;
        request.FeaturedMedia = localEditableMeta.FeaturedMedia;
        request.CommentStatus = localEditableMeta.CommentStatus;
        request.PingStatus = localEditableMeta.PingStatus;

        var (allCategories, allTags) = _cacheService.GetTaxonomies();
        var validCategoryIds = new HashSet<int>(allCategories.Select(c => c.Id));
        var validTagIds = new HashSet<int>(allTags.Select(t => t.Id));

        if (!TryResolveTaxonomyIds(id, localEditableMeta.Categories, validCategoryIds, "Category", out var categoryIds, out var catError))
        {
            throw new InvalidOperationException(catError!);
        }
        if (!TryResolveTaxonomyIds(id, localEditableMeta.Tags, validTagIds, "Tag", out var tagIds, out var tagError))
        {
            throw new InvalidOperationException(tagError!);
        }

        request.Categories = categoryIds;
        request.Tags = tagIds;

        var updatedPost = await _wpService.UpdatePostAsync(id, request, cancellationToken);
        var cacheResult = _cacheService.SavePostToCache(updatedPost);
        return (updatedPost, cacheResult);
    }

    public async Task<TransferReport> PushAllModifiedPostsAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        var report = new TransferReport();
        var localMetas = _cacheService.ListLocalPostMetadata();

        foreach (var localMeta in localMetas)
        {
            var id = localMeta.Post.Id;
            try
            {
                if (!_cacheService.IsPostCacheFilePresent(id))
                {
                    continue; // Skip if the file doesn't exist for some reason
                }

                var localPost = _cacheService.ReadLocalPost(id);
                if (localPost == null) continue;

                var fullLocalContent = string.Join("\n", "---", _cacheService.SerializeToYaml(localPost.Metadata), "---", "", localPost.Content);
                var currentLocalHash = _cacheService.ComputeSha256Hash(fullLocalContent);
                var isLocalChanged = currentLocalHash != localMeta.FileHash;

                if (isLocalChanged)
                {
                    Console.WriteLine($"Local changes detected for post {id}. Pushing to server...");
                    var (updatedPost, cacheResult) = await PushPostAsync(id, profile, cancellationToken);
                    if (cacheResult.WasMoved)
                    {
                        report.MovedPosts.Add(cacheResult.MoveMessage!);
                    }
                    report.PushedToServer.Add(id);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to push post {id}: {ex.Message}");
                report.LocalValidationErrors.Add((id, ex.Message));
            }
        }

        return report;
    }

    public async Task<WordPressCategory> PushCategoryAsync(int id, CancellationToken cancellationToken)
    {
        var local = await _cacheService.GetLocalTaxonomyTermAsync<EditableCategory>("category", id)
            ?? throw new InvalidOperationException($"Could not find local category with ID {id}.");
        var request = new WordPressUpdateCategoryRequest
        {
            Name = local.Name,
            Slug = local.Slug,
            Description = local.Description
        };
        var updated = await _wpService.UpdateCategoryAsync(id, request, cancellationToken);
        await _cacheService.UpdateLocalTaxonomyTermAsync(updated, updateHashOnly: true);
        return updated;
    }

    public async Task<WordPressTag> PushTagAsync(int id, CancellationToken cancellationToken)
    {
        var local = await _cacheService.GetLocalTaxonomyTermAsync<EditableTag>("tag", id)
            ?? throw new InvalidOperationException($"Could not find local tag with ID {id}.");
        var request = new WordPressUpdateTagRequest
        {
            Name = local.Name,
            Slug = local.Slug,
            Description = local.Description
        };
        var updated = await _wpService.UpdateTagAsync(id, request, cancellationToken);
        await _cacheService.UpdateLocalTaxonomyTermAsync(updated, updateHashOnly: true);
        return updated;
    }

    public async Task<WordPressMedia> PushMediaAsync(int id, CancellationToken cancellationToken)
    {
        // Read local YAML metadata
        var all = _cacheService.ReadLocalMediaMetadata();
        var entry = all.FirstOrDefault(x => x.MediaId == id);
        if (entry.MediaId == 0)
        {
            throw new InvalidOperationException($"Could not find local media metadata for ID {id}.");
        }

        var req = new WordPressUpdateMediaRequest
        {
            Title = entry.Metadata.Title,
            Description = entry.Metadata.Description,
            Caption = entry.Metadata.Caption,
            AltText = entry.Metadata.AltText
        };
        var updated = await _wpService.UpdateMediaAsync(id, req, cancellationToken);
        _cacheService.UpdateMediaMetadataOnly(updated);
        return updated;
    }

    // --- Shared Operations for CLI and MCP ---

    public async Task<(WordPressPostDetail Post, PostCacheResult? CacheResult)> CreatePostAsync(
        string title,
        string? content,
        string? contentFilePath,
        string? status,
        string? editMode,
        int[]? categories,
        int[]? tags,
        int? featuredMedia,
        ConnectionProfile profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required and cannot be empty.", nameof(title));
        }

        var bodyContent = content;
        if (string.IsNullOrWhiteSpace(bodyContent) && !string.IsNullOrWhiteSpace(contentFilePath) && File.Exists(contentFilePath))
        {
            bodyContent = await File.ReadAllTextAsync(contentFilePath, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(bodyContent))
        {
            throw new ArgumentException("Either content or a valid content file is required.", nameof(content));
        }

        var resolvedStatus = string.IsNullOrWhiteSpace(status) ? "draft" : status;
        var resolvedEditMode = string.IsNullOrWhiteSpace(editMode) ? "markdown" : editMode.ToLowerInvariant();
        if (resolvedEditMode != "markdown" && resolvedEditMode != "html")
        {
            throw new ArgumentException("Invalid value for edit-mode. Must be 'markdown' or 'html'.", nameof(editMode));
        }

        var request = new WordPressCreatePostRequest
        {
            Title = title,
            Status = resolvedStatus,
            Categories = categories,
            Tags = tags,
            FeaturedMedia = featuredMedia
        };

        var conversion = profile.MarkdownConversion ?? "client";
        if (resolvedEditMode == "markdown")
        {
            request.Meta = new Dictionary<string, object?> { { "_md_source", bodyContent ?? string.Empty } };
            request.Content = conversion == "client" ? Markdown.ToHtml(bodyContent ?? string.Empty) : bodyContent;
        }
        else
        {
            request.Content = bodyContent;
        }

        var post = await _wpService.CreatePostAsync(request, cancellationToken).ConfigureAwait(false);
        PostCacheResult? cacheResult = null;
        if (!string.IsNullOrEmpty(profile.CachePath))
        {
            cacheResult = _cacheService.SavePostToCache(post);
            _cacheService.OrganizePostFiles();
        }
        return (post, cacheResult);
    }

    public async Task<(WordPressDeleteResponse Response, string? CachedTitle, bool AlreadyDeleted)> DeletePostAsync(
        int id,
        bool force,
        CancellationToken cancellationToken)
    {
        var cachedTitle = _cacheService.GetCachedPostTitle(id);
        try
        {
            var response = await _wpService.DeletePostAsync(id, force, cancellationToken).ConfigureAwait(false);
            if (response.Deleted)
            {
                _cacheService.DeletePostFromCache(id);
            }
            return (response, cachedTitle, false);
        }
        catch (WordPressApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var title = cachedTitle ?? "Unknown (already deleted)";
            _cacheService.DeletePostFromCache(id);
            var fallbackResponse = new WordPressDeleteResponse
            {
                Deleted = true,
                Previous = new Dictionary<string, JsonElement>
                {
                    ["id"] = JsonSerializer.SerializeToElement(id),
                    ["title"] = JsonSerializer.SerializeToElement(new { raw = title })
                }
            };
            return (fallbackResponse, title, true);
        }
    }

    public async Task<WordPressCategory> CreateCategoryAsync(
        string name,
        string? slug,
        string? description,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.", nameof(name));
        }

        var request = new WordPressCreateCategoryRequest
        {
            Name = name,
            Slug = slug,
            Description = description
        };

        var category = await _wpService.CreateCategoryAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            _cacheService.SaveCategoryToCache(category);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to write category cache: {ex.Message}");
        }
        return category;
    }

    public async Task<(WordPressDeleteResponse Response, string? CachedName, bool AlreadyDeleted)> DeleteCategoryAsync(
        int id,
        bool force,
        CancellationToken cancellationToken)
    {
        var cachedName = _cacheService.GetCachedCategoryName(id);
        try
        {
            var response = await _wpService.DeleteCategoryAsync(id, force, cancellationToken).ConfigureAwait(false);
            if (response.Deleted)
            {
                _cacheService.DeleteCategoryFromCache(id);
            }
            return (response, cachedName, false);
        }
        catch (WordPressApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var name = cachedName ?? "Unknown (already deleted)";
            _cacheService.DeleteCategoryFromCache(id);
            var fallbackResponse = new WordPressDeleteResponse
            {
                Deleted = true,
                Previous = new Dictionary<string, JsonElement>
                {
                    ["id"] = JsonSerializer.SerializeToElement(id),
                    ["name"] = JsonSerializer.SerializeToElement(name)
                }
            };
            return (fallbackResponse, name, true);
        }
    }

    public async Task<WordPressTag> CreateTagAsync(
        string name,
        string? slug,
        string? description,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tag name is required.", nameof(name));
        }

        var request = new WordPressCreateTagRequest
        {
            Name = name,
            Slug = slug,
            Description = description
        };

        var tag = await _wpService.CreateTagAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            _cacheService.SaveTagToCache(tag);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to write tag cache: {ex.Message}");
        }
        return tag;
    }

    public async Task<(WordPressDeleteResponse Response, string? CachedName, bool AlreadyDeleted)> DeleteTagAsync(
        int id,
        bool force,
        CancellationToken cancellationToken)
    {
        var cachedName = _cacheService.GetCachedTagName(id);
        try
        {
            var response = await _wpService.DeleteTagAsync(id, force, cancellationToken).ConfigureAwait(false);
            if (response.Deleted)
            {
                _cacheService.DeleteTagFromCache(id);
            }
            return (response, cachedName, false);
        }
        catch (WordPressApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var name = cachedName ?? "Unknown (already deleted)";
            _cacheService.DeleteTagFromCache(id);
            var fallbackResponse = new WordPressDeleteResponse
            {
                Deleted = true,
                Previous = new Dictionary<string, JsonElement>
                {
                    ["id"] = JsonSerializer.SerializeToElement(id),
                    ["name"] = JsonSerializer.SerializeToElement(name)
                }
            };
            return (fallbackResponse, name, true);
        }
    }

    public async Task<(WordPressDeleteResponse Response, string? CachedTitle, bool AlreadyDeleted)> DeleteMediaAsync(
        int id,
        bool force,
        CancellationToken cancellationToken)
    {
        var cachedTitle = _cacheService.GetCachedMediaTitle(id);
        try
        {
            var response = await _wpService.DeleteMediaAsync(id, force, cancellationToken).ConfigureAwait(false);
            if (response.Deleted)
            {
                _cacheService.DeleteMediaFromCache(id);
            }
            return (response, cachedTitle, false);
        }
        catch (WordPressApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var title = cachedTitle ?? "Unknown (already deleted)";
            _cacheService.DeleteMediaFromCache(id);
            var fallbackResponse = new WordPressDeleteResponse
            {
                Deleted = true,
                Previous = new Dictionary<string, JsonElement>
                {
                    ["id"] = JsonSerializer.SerializeToElement(id),
                    ["title"] = JsonSerializer.SerializeToElement(new { raw = title })
                }
            };
            return (fallbackResponse, title, true);
        }
    }

    // --- Taxonomy Pull & Sync ---

    public async Task<TransferReport> PullTaxonomiesAsync(CancellationToken cancellationToken)
    {
        var report = new TransferReport();
        var allCategories = await _wpService.ListCategoriesAsync(cancellationToken);
        var allTags = await _wpService.ListTagsAsync(cancellationToken);
        await _cacheService.UpdateTaxonomiesCacheAsync(allCategories, allTags);
        report.PulledTaxonomies.Add($"Cached {allCategories.Count} categories and {allTags.Count} tags");
        return report;
    }

    public async Task<TransferReport> SyncTaxonomiesAsync(CancellationToken cancellationToken)
    {
        var report = new TransferReport();
        // 1. Push local taxonomy changes first
        await SynchronizeLocalTaxonomyChangesAsync(report, cancellationToken);
        // 2. Synchronize taxonomies from server (pull changes and update local state)
        var allCategories = await _wpService.ListCategoriesAsync(cancellationToken);
        var allTags = await _wpService.ListTagsAsync(cancellationToken);
        await _cacheService.UpdateTaxonomiesCacheAsync(allCategories, allTags);
        report.PulledTaxonomies.Add($"Cached {allCategories.Count} categories and {allTags.Count} tags");
        return report;
    }

    // Alias for backward compatibility
    public Task<TransferReport> SynchronizeTaxonomiesAsync(CancellationToken cancellationToken)
        => SyncTaxonomiesAsync(cancellationToken);

    // --- Post Pull & Sync ---

    public Task<TransferReport> PullPostsAsync(ConnectionProfile profile, int syncLimit, CancellationToken cancellationToken)
        => ProcessPostsSyncOrPullAsync(profile, syncLimit, allowPush: false, cancellationToken);

    public Task<TransferReport> SyncPostsAsync(ConnectionProfile profile, int syncLimit, CancellationToken cancellationToken)
        => ProcessPostsSyncOrPullAsync(profile, syncLimit, allowPush: true, cancellationToken);

    // Alias for backward compatibility
    public Task<TransferReport> SynchronizePostsAsync(ConnectionProfile profile, int syncLimit, CancellationToken cancellationToken)
        => SyncPostsAsync(profile, syncLimit, cancellationToken);

    private async Task<TransferReport> ProcessPostsSyncOrPullAsync(ConnectionProfile profile, int syncLimit, bool allowPush, CancellationToken cancellationToken)
    {
        var report = new TransferReport();

        if (allowPush)
        {
            Console.WriteLine("Step 1/2: Synchronizing taxonomies (categories and tags)...");
            var taxReport = await SyncTaxonomiesAsync(cancellationToken);
            report.PushedTaxonomies.AddRange(taxReport.PushedTaxonomies);
            report.PulledTaxonomies.AddRange(taxReport.PulledTaxonomies);
            Console.WriteLine("Taxonomy synchronization complete.");
        }
        else
        {
            Console.WriteLine("Step 1/2: Pulling taxonomies (categories and tags)...");
            var taxReport = await PullTaxonomiesAsync(cancellationToken);
            report.PulledTaxonomies.AddRange(taxReport.PulledTaxonomies);
            Console.WriteLine("Taxonomy pull complete.");
        }

        Console.WriteLine(allowPush ? "Step 2/2: Synchronizing posts (two-way)..." : "Step 2/2: Pulling posts from server...");
        var localPosts = _cacheService.ListLocalPostMetadata()
            .ToDictionary(meta => meta.Post.Id, meta => meta);

        var publishPosts = await _wpService.ListPostsAsync(
                status: "publish",
                perPage: syncLimit,
                page: 1,
                cancellationToken);
        
        var draftPosts = await _wpService.ListPostsAsync(
                status: "draft",
                perPage: syncLimit,
                page: 1,
                cancellationToken);

        var allRemotePosts = publishPosts.Concat(draftPosts);

        var topNRemotePosts = allRemotePosts
            .GroupBy(post => post.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var allIds = localPosts.Keys.Union(topNRemotePosts.Keys).ToList();

        foreach (var id in allIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hasLocal = localPosts.TryGetValue(id, out var localMeta);
            var hasRemoteInTopN = topNRemotePosts.TryGetValue(id, out var remotePostFromTopN);

            if (hasLocal && hasRemoteInTopN)
            {
                await CompareAndSyncAsync(id, localMeta!, remotePostFromTopN!, profile, report, allowPush, cancellationToken);
            }
            else if (!hasLocal && hasRemoteInTopN)
            {
                _cacheService.SavePostToCache(remotePostFromTopN!);
                report.NewlyCached.Add(id);
            }
            else if (hasLocal && !hasRemoteInTopN)
            {
                var localPost = _cacheService.ReadLocalPost(id);
                if (localPost == null) continue;

                var fullLocalContent = string.Join("\n", "---", _cacheService.SerializeToYaml(localPost.Metadata), "---", "", localPost.Content);
                var currentLocalHash = _cacheService.ComputeSha256Hash(fullLocalContent);
                var isLocalChanged = currentLocalHash != localMeta!.FileHash;

                try
                {
                    var remotePost = await _wpService.GetPostAsync(id, cancellationToken);
                    if (remotePost != null)
                    {
                        await CompareAndSyncAsync(id, localMeta!, remotePost, profile, report, allowPush, cancellationToken);
                    }
                }
                catch (WordPressApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    if (!isLocalChanged)
                    {
                        _cacheService.DeletePostFromCache(id);
                        report.DeletedFromLocal.Add(id);
                    }
                    else
                    {
                        // If there are local edits, keep local to avoid data loss
                        report.LocalEditsKept.Add(id);
                    }
                }
            }
        }

        _cacheService.OrganizePostFiles();
        Console.WriteLine(allowPush ? "Post synchronization complete." : "Post pull complete.");

        return report;
    }

    // --- Media Pull & Sync ---

    public Task<TransferReport> PullMediaAsync(int syncLimit, CancellationToken cancellationToken)
        => ProcessMediaSyncOrPullAsync(syncLimit, allowPush: false, cancellationToken);

    public Task<TransferReport> SyncMediaAsync(int syncLimit, CancellationToken cancellationToken)
        => ProcessMediaSyncOrPullAsync(syncLimit, allowPush: true, cancellationToken);

    public Task<TransferReport> SynchronizeMediaAsync(int syncLimit, CancellationToken cancellationToken)
        => SyncMediaAsync(syncLimit, cancellationToken);

    private async Task<TransferReport> ProcessMediaSyncOrPullAsync(int syncLimit, bool allowPush, CancellationToken cancellationToken)
    {
        var report = new TransferReport();

        // 1. Push local metadata changes first (only in two-way sync mode)
        if (allowPush)
        {
            var localMediaIds = _cacheService.ReadLocalMediaMetadata().Select(m => m.MediaId).ToList();

            foreach (var mediaId in localMediaIds)
            {
                if (_cacheService.IsLocalMediaChanged(mediaId))
                {
                    try
                    {
                        var metadata = _cacheService.ReadLocalMediaMetadata().FirstOrDefault(m => m.MediaId == mediaId).Metadata;
                        if (metadata == null) continue;

                        var request = new WordPressUpdateMediaRequest
                        {
                            Title = metadata.Title,
                            Description = metadata.Description,
                            Caption = metadata.Caption,
                            AltText = metadata.AltText
                        };

                        var updatedMedia = await _wpService.UpdateMediaAsync(mediaId, request, cancellationToken);
                        _cacheService.UpdateMediaMetadataOnly(updatedMedia);
                        report.PushedMediaToServer.Add(mediaId);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to push media metadata for ID {mediaId}: {ex.Message}");
                        report.MediaConflicts.Add(mediaId);
                    }
                }
            }
        }

        // 2. Fetch remote and local states
        var remoteMedia = (await _wpService.ListMediaAsync(perPage: syncLimit, page: 1, cancellationToken))
            .ToDictionary(m => m.Id, m => m);
        
        var localMediaMetadata = _cacheService.ReadLocalMediaMetadata().ToDictionary(m => m.MediaId, m => m.Metadata);

        var allIds = localMediaMetadata.Keys.Union(remoteMedia.Keys).ToList();

        // 3. Compare and sync each item
        foreach (var id in allIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hasLocal = localMediaMetadata.TryGetValue(id, out var localMeta);
            var hasRemote = remoteMedia.TryGetValue(id, out var remoteMeta);

            if (hasLocal && hasRemote)
            {
                var isLocalChanged = _cacheService.IsLocalMediaChanged(id);
                var isRemoteChanged = (remoteMeta!.ModifiedGmt.GetValueOrDefault() - localMeta!.ModifiedGmt.GetValueOrDefault()).TotalSeconds > 1;

                if (isLocalChanged && isRemoteChanged)
                {
                    report.MediaConflicts.Add(id);
                }
                else if (isRemoteChanged)
                {
                    await PullMediaItemAsync(remoteMeta, report, cancellationToken);
                }
            }
            else if (!hasLocal && hasRemote)
            {
                await PullMediaItemAsync(remoteMeta, report, cancellationToken);
            }
            else if (hasLocal && !hasRemote)
            {
                if (_cacheService.IsLocalMediaChanged(id))
                {
                    if (allowPush)
                    {
                        report.MediaConflicts.Add(id);
                    }
                    else
                    {
                        report.LocalEditsKept.Add(id);
                    }
                    continue;
                }

                try
                {
                    await _wpService.GetMediaAsync(id, cancellationToken);
                }
                catch (WordPressApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    _cacheService.DeleteMediaFromCache(id);
                    report.DeletedFromLocal.Add(id);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error probing media item {id}: {ex.Message}");
                    report.MediaConflicts.Add(id);
                }
            }
        }

        return report;
    }

        private async Task PullMediaItemAsync(WordPressMedia? media, TransferReport report, CancellationToken cancellationToken)
        {
            if (media == null) return;

            if (string.IsNullOrEmpty(media.SourceUrl))
            {
                report.MediaConflicts.Add(media.Id);
                return;
            }

            try
            {
                var fileContent = await _wpService.DownloadMediaFileAsync(media.SourceUrl, cancellationToken);
                var isNew = !_cacheService.IsMediaCached(media.Id);
                _cacheService.SaveMediaToCache(media, fileContent);

                if (isNew)
                {
                    report.NewlyCachedMedia.Add(media.Id);
                }
                else
                {
                    report.PulledMediaFromServer.Add(media.Id);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to sync media item {media.Id}: {ex.Message}");
                report.MediaConflicts.Add(media.Id);
            }
        }

    private async Task SynchronizeLocalTaxonomyChangesAsync(TransferReport report, CancellationToken cancellationToken)
    {
        // 1. Read local taxonomy files
        var (localCategories, localTags) = _cacheService.ReadLocalTaxonomies();

        // 2. Synchronize Categories
        var (cachedCategories, cachedTags) = _cacheService.GetTaxonomies();
        var cachedCategoriesDict = cachedCategories.ToDictionary(c => c.Id);

        foreach (var (localCat, rawContent) in localCategories)
        {
            if (localCat.Id == 0)
            {
                // Create new category
                var newCat = await _wpService.CreateCategoryAsync(new WordPressCreateCategoryRequest { Name = localCat.Name, Slug = localCat.Slug, Description = localCat.Description }, cancellationToken);
                report.PushedTaxonomies.Add($"Created Category: {newCat.Name}");
            }
            else if (cachedCategoriesDict.TryGetValue(localCat.Id, out var cachedCat))
            {
                // Update existing category
                var currentHash = _cacheService.ComputeSha256Hash(rawContent);
                var previousHash = _cacheService.GetState($"category_{localCat.Id}_hash");

                if (currentHash != previousHash)
                {
                    await _wpService.UpdateCategoryAsync(localCat.Id, new WordPressUpdateCategoryRequest { Name = localCat.Name, Slug = localCat.Slug, Description = localCat.Description }, cancellationToken);
                    report.PushedTaxonomies.Add($"Updated Category: {localCat.Name}");
                }
            }
        }

        // 3. Synchronize Tags
        var cachedTagsDict = cachedTags.ToDictionary(t => t.Id);
        foreach (var (localTag, rawContent) in localTags)
        {
            if (localTag.Id == 0)
            {
                // Create new tag
                var newTag = await _wpService.CreateTagAsync(new WordPressCreateTagRequest { Name = localTag.Name, Slug = localTag.Slug, Description = localTag.Description }, cancellationToken);
                report.PushedTaxonomies.Add($"Created Tag: {newTag.Name}");
            }
            else if (cachedTagsDict.TryGetValue(localTag.Id, out var cachedTag))
            {
                // Update existing tag
                var currentHash = _cacheService.ComputeSha256Hash(rawContent);
                var previousHash = _cacheService.GetState($"tag_{localTag.Id}_hash");

                if (currentHash != previousHash)
                {
                    await _wpService.UpdateTagAsync(localTag.Id, new WordPressUpdateTagRequest { Name = localTag.Name, Slug = localTag.Slug, Description = localTag.Description }, cancellationToken);
                    report.PushedTaxonomies.Add($"Updated Tag: {localTag.Name}");
                }
            }
        }
    }

    private async Task CompareAndSyncAsync(int id, CachePostMetadata localMeta, WordPressPostDetail remotePost, ConnectionProfile profile, TransferReport report, bool allowPush, CancellationToken cancellationToken)
    {
        var cacheFileExists = _cacheService.IsPostCacheFilePresent(id);

        if (!cacheFileExists)
        {
            // If the main cache file doesn't exist, but we have a DB record, it's an incomplete cache.
            // We can't know if there were local changes, so to be safe, we declare a conflict.
            report.ConflictDetected.Add(id);
        }
        else
        {
            // 1. Check for local changes by comparing hashes
            var localPost = _cacheService.ReadLocalPost(id);
            if (localPost == null || localPost.Metadata == null)
            {
                // This case should be rare, but if file disappears between check and read, pull from server.
                _cacheService.SavePostToCache(remotePost);
                report.PulledFromServer.Add(id);
                return;
            }

            var fullLocalContent = string.Join("\n", "---", _cacheService.SerializeToYaml(localPost.Metadata), "---", "", localPost.Content);
            var currentLocalHash = _cacheService.ComputeSha256Hash(fullLocalContent);
            var isLocalChanged = currentLocalHash != localMeta.FileHash;

            // 2. Check for remote changes using modification timestamp
            var lastSyncServerModified = localMeta.Post.Modified.GetValueOrDefault();
            var currentServerModified = remotePost.Modified.GetValueOrDefault();
            var isServerChanged = (currentServerModified - lastSyncServerModified).TotalSeconds > 1;

            // 3. Determine action
            if (isLocalChanged && isServerChanged)
            {
                report.ConflictDetected.Add(id);
            }
            else if (isLocalChanged)
            {
                if (!allowPush)
                {
                    // Pure pull: protect local edits from being overwritten or pushed
                    report.LocalEditsKept.Add(id);
                    return;
                }

                var request = new WordPressUpdatePostRequest();
                var localEditableMeta = localPost.Metadata;

                if (localEditableMeta == null) {
                    report.ConflictDetected.Add(id);
                    return;
                }

                // Initialize Meta from local file, then add/overwrite internal fields
                var metaForRequest = localEditableMeta.Meta ?? new Dictionary<string, object?>();

                var editMode = localEditableMeta.EditMode ?? "html";
                var conversion = profile.MarkdownConversion ?? "client";

                if (editMode == "markdown")
                {
                    metaForRequest["_md_source"] = localPost.Content;
                    request.Content = conversion == "client" ? Markdown.ToHtml(localPost.Content) : localPost.Content;
                }
                else // html mode
                {
                    request.Content = localPost.Content;
                }

                request.Meta = metaForRequest;

                request.Title = localEditableMeta.Title;
                request.Slug = localEditableMeta.Slug;
                request.Status = localEditableMeta.Status;
                if (DateTime.TryParse(localEditableMeta.Date, out var localDate))
                {
                    request.Date = localDate;
                }
                request.Excerpt = localEditableMeta.Excerpt;
                request.FeaturedMedia = localEditableMeta.FeaturedMedia;
                request.CommentStatus = localEditableMeta.CommentStatus;
                request.PingStatus = localEditableMeta.PingStatus;

                var (allCategories, allTags) = _cacheService.GetTaxonomies();
                var validCategoryIds = new HashSet<int>(allCategories.Select(c => c.Id));
                var validTagIds = new HashSet<int>(allTags.Select(t => t.Id));

                if (!TryResolveTaxonomyIds(id, localEditableMeta.Categories, validCategoryIds, "Category", out var categoryIds, out var catError))
                {
                    report.LocalValidationErrors.Add((id, catError!));
                    return;
                }
                if (!TryResolveTaxonomyIds(id, localEditableMeta.Tags, validTagIds, "Tag", out var tagIds, out var tagError))
                {
                    report.LocalValidationErrors.Add((id, tagError!));
                    return;
                }

                request.Categories = categoryIds;
                request.Tags = tagIds;

                var updatedPost = await _wpService.UpdatePostAsync(id, request, cancellationToken);
                var pushCacheResult = _cacheService.SavePostToCache(updatedPost);
                if (pushCacheResult.WasMoved)
                {
                    report.MovedPosts.Add(pushCacheResult.MoveMessage!);
                }
                report.PushedToServer.Add(id);
            }
            else if (isServerChanged)
            {
                var pullCacheResult = _cacheService.SavePostToCache(remotePost);
                if (pullCacheResult.WasMoved)
                {
                    report.MovedPosts.Add(pullCacheResult.MoveMessage!);
                }
                report.PulledFromServer.Add(id);
            }
        }
    }

    private bool TryResolveTaxonomyIds(int postId, List<string>? namesOrIds, HashSet<int> validIds, string taxonomyType, out int[]? resolvedIds, out string? errorMessage)
    {
        resolvedIds = null;
        errorMessage = null;
        if (namesOrIds == null) return true;

        var idList = new List<int>();
        foreach (var item in namesOrIds)
        {
            int id;
            var parts = item.Split(new[] { '-' }, 2);

            if (parts.Length == 2 && int.TryParse(parts[0], out id))
            {
                // Parsed 'ID-Name' format successfully
            }
            else if (int.TryParse(item, out id))
            {
                // Parsed plain numeric ID successfully
            }
            else
            {
                errorMessage = $"Post {postId}: Invalid {taxonomyType} format for '{item}'. Expected 'ID-Name' or a numeric ID.";
                resolvedIds = null;
                return false;
            }

            if (!validIds.Contains(id))
            {
                errorMessage = $"Post {postId}: {taxonomyType} with ID '{id}' (from '{item}') does not exist. Please check the ID.";
                resolvedIds = null;
                return false;
            }
            
            idList.Add(id);
        }

        resolvedIds = idList.ToArray();
        return true;
    }

    public async Task<string> ResolveConflictAsync(string type, int id, string strategy, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        switch (type.ToLowerInvariant())
        {
            case "post":
                return await ResolvePostConflictAsync(id, strategy, profile, cancellationToken);
            case "category":
            case "tag":
                // TODO: Implement taxonomy conflict resolution
                return await ResolveTaxonomyConflictAsync(type, id, strategy, cancellationToken);
            default:
                throw new ArgumentException($"Unsupported conflict type: {type}");
        }
    }

    private async Task<string> ResolvePostConflictAsync(int id, string strategy, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Resolving conflict for post {id} with strategy: {strategy}...");

        if (strategy == "server-wins")
        {
            var remotePost = await _wpService.GetPostAsync(id, cancellationToken);
            var cacheResult = _cacheService.SavePostToCache(remotePost);
            if (cacheResult.WasMoved)
            {
                sb.AppendLine(cacheResult.MoveMessage);
            }
            sb.AppendLine($"Conflict resolved. Local post {id} was overwritten with the server version.");
        }
        else if (strategy == "local-wins")
        {
            var localPost = _cacheService.ReadLocalPost(id)
                ?? throw new InvalidOperationException($"Could not read local post data for {id}. Cannot push local changes.");

            var request = new WordPressUpdatePostRequest();
            var localEditableMeta = localPost.Metadata;

            // Initialize Meta from local file, then add/overwrite internal fields
            var metaForRequest = localEditableMeta.Meta ?? new Dictionary<string, object?>();

            // Handle content based on edit mode
            var editMode = localEditableMeta.EditMode ?? "html";
            var conversion = profile.MarkdownConversion ?? "client";

            if (editMode == "markdown")
            {
                metaForRequest["_md_source"] = localPost.Content;
                request.Content = conversion == "client" ? Markdown.ToHtml(localPost.Content) : localPost.Content;
            }
            else // html mode
            {
                request.Content = localPost.Content;
            }

            request.Meta = metaForRequest;

            request.Title = localEditableMeta.Title;
            request.Slug = localEditableMeta.Slug;
            request.Status = localEditableMeta.Status;
            if (DateTime.TryParse(localEditableMeta.Date, out var localDate))
            {
                request.Date = localDate;
            }
            request.Excerpt = localEditableMeta.Excerpt;
            request.FeaturedMedia = localEditableMeta.FeaturedMedia;
            request.CommentStatus = localEditableMeta.CommentStatus;
            request.PingStatus = localEditableMeta.PingStatus;

            var (allCategories, allTags) = _cacheService.GetTaxonomies();
            var validCategoryIds = new HashSet<int>(allCategories.Select(c => c.Id));
            var validTagIds = new HashSet<int>(allTags.Select(t => t.Id));

            if (!TryResolveTaxonomyIds(id, localEditableMeta.Categories, validCategoryIds, "Category", out var categoryIds, out var catError))
            {
                throw new InvalidOperationException(catError!);
            }
            if (!TryResolveTaxonomyIds(id, localEditableMeta.Tags, validTagIds, "Tag", out var tagIds, out var tagError))
            {
                throw new InvalidOperationException(tagError!);
            }

            request.Categories = categoryIds;
            request.Tags = tagIds;

            var updatedPost = await _wpService.UpdatePostAsync(id, request, cancellationToken);
            var cacheResult = _cacheService.SavePostToCache(updatedPost);
            if (cacheResult.WasMoved)
            {
                sb.AppendLine(cacheResult.MoveMessage);
            }
            sb.AppendLine($"Conflict resolved. Server post {id} was overwritten with the local version.");
        }

        var organized = _cacheService.OrganizePostFiles();
        foreach (var msg in organized)
        {
            sb.AppendLine($"[Cache] {msg}");
        }
        return sb.ToString();
    }

    private async Task<string> ResolveTaxonomyConflictAsync(string type, int id, string strategy, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Resolving conflict for {type} {id} with strategy: {strategy}...");

        if (strategy == "server-wins")
        {
            if (type == "category")
            {
                var remoteTerm = await _wpService.GetCategoryAsync(id, cancellationToken);
                await _cacheService.UpdateLocalTaxonomyTermAsync(remoteTerm);
            }
            else // tag
            {
                var remoteTerm = await _wpService.GetTagAsync(id, cancellationToken);
                await _cacheService.UpdateLocalTaxonomyTermAsync(remoteTerm);
            }
            sb.AppendLine($"Conflict resolved. Local {type} {id} was overwritten with the server version.");
        }
        else if (strategy == "local-wins")
        {
            if (type == "category")
            {
                var localTerm = await _cacheService.GetLocalTaxonomyTermAsync<EditableCategory>(type, id);
                if (localTerm == null) throw new InvalidOperationException($"Could not find local category with ID {id}.");

                var request = new WordPressUpdateCategoryRequest { Name = localTerm.Name, Slug = localTerm.Slug, Description = localTerm.Description };
                var updatedTerm = await _wpService.UpdateCategoryAsync(id, request, cancellationToken);
                await _cacheService.UpdateLocalTaxonomyTermAsync(updatedTerm, updateHashOnly: true);
            }
            else // tag
            {
                var localTerm = await _cacheService.GetLocalTaxonomyTermAsync<EditableTag>(type, id);
                if (localTerm == null) throw new InvalidOperationException($"Could not find local tag with ID {id}.");

                var request = new WordPressUpdateTagRequest { Name = localTerm.Name, Slug = localTerm.Slug, Description = localTerm.Description };
                var updatedTerm = await _wpService.UpdateTagAsync(id, request, cancellationToken);
                await _cacheService.UpdateLocalTaxonomyTermAsync(updatedTerm, updateHashOnly: true);
            }
            sb.AppendLine($"Conflict resolved. Server {type} {id} was overwritten with the local version.");
        }
        return sb.ToString();
    }
}
