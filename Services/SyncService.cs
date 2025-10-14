
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

public class SyncReport
{
    public List<int> PushedToServer { get; } = new();
    public List<int> PulledFromServer { get; } = new();
    public List<int> DeletedFromLocal { get; } = new();
    public List<int> ConflictDetected { get; } = new();
    public List<int> NewlyCached { get; } = new();
    public List<string> PushedTaxonomies { get; } = new();

    // Media Sync Properties
    public List<int> PushedMediaToServer { get; } = new();
    public List<int> PulledMediaFromServer { get; } = new();
    public List<int> NewlyCachedMedia { get; } = new();
    public List<int> DeletedMediaFromLocal { get; } = new();
    public List<int> MediaConflicts { get; } = new();
}

public class SyncService
{
    private readonly WordPressService _wpService;
    private readonly CacheService _cacheService;
    
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public SyncService(WordPressService wpService, CacheService cacheService)
    {
        _wpService = wpService;
        _cacheService = cacheService;
    }

    // Push a single post using local cache (content.md + editable.yaml)
    public async Task<WordPressPostDetail> PushPostAsync(int id, ConnectionProfile profile, CancellationToken cancellationToken)
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
        request.Date = localEditableMeta.Date;
        request.Excerpt = localEditableMeta.Excerpt;
        request.FeaturedMedia = localEditableMeta.FeaturedMedia;
        request.CommentStatus = localEditableMeta.CommentStatus;
        request.PingStatus = localEditableMeta.PingStatus;

        if (!TryResolveTaxonomyIds(localEditableMeta.Categories, _cacheService.FindCategoryId, out var categoryIds) ||
            !TryResolveTaxonomyIds(localEditableMeta.Tags, _cacheService.FindTagId, out var tagIds))
        {
            throw new InvalidOperationException($"Failed to resolve taxonomy IDs for post {id}. Please check the category and tag names in the local file.");
        }
        request.Categories = categoryIds;
        request.Tags = tagIds;

        var updatedPost = await _wpService.UpdatePostAsync(id, request, cancellationToken);
        _cacheService.SavePostToCache(updatedPost);
        return updatedPost;
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

    public async Task<SyncReport> SynchronizeTaxonomiesAsync(CancellationToken cancellationToken)
    {
        var report = new SyncReport();
        // 1. Push local taxonomy changes first
        await SynchronizeLocalTaxonomyChangesAsync(report, cancellationToken);
        // 2. Synchronize taxonomies from server (pull changes and update local state)
        var allCategories = await _wpService.ListCategoriesAsync(cancellationToken);
        var allTags = await _wpService.ListTagsAsync(cancellationToken);
        await _cacheService.UpdateTaxonomiesCacheAsync(allCategories, allTags);
        return report;
    }

    public async Task<SyncReport> SynchronizePostsAsync(ConnectionProfile profile, int syncLimit, CancellationToken cancellationToken)
    {
        var report = new SyncReport();

        // 3. Synchronize posts
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
                await CompareAndSyncAsync(id, localMeta!, remotePostFromTopN!, profile, report, cancellationToken);
            }
            else if (!hasLocal && hasRemoteInTopN)
            {
                _cacheService.SavePostToCache(remotePostFromTopN!);
                report.NewlyCached.Add(id);
            }
            else if (hasLocal && !hasRemoteInTopN)
            {
                var localPost = _cacheService.ReadLocalPost(id);
                if (localPost == null) continue; // Should not happen if hasLocal is true

                var fullLocalContent = string.Join("\n", "---", _cacheService.SerializeToYaml(localPost.Metadata), "---", "", localPost.Content);
                var currentLocalHash = _cacheService.ComputeSha256Hash(fullLocalContent);
                var isLocalChanged = currentLocalHash != localMeta!.FileHash;

                try
                {
                    // Check the server for existence regardless of local changes.
                    // - If it exists and local changed: reconcile via CompareAndSyncAsync
                    // - If it does not exist (404): delete local cache if local is unchanged
                    var remotePost = await _wpService.GetPostAsync(id, cancellationToken);
                    if (remotePost != null && isLocalChanged)
                    {
                        await CompareAndSyncAsync(id, localMeta!, remotePost, profile, report, cancellationToken);
                    }
                }
                catch (WordPressApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // Server-side post no longer exists. Delete local cache only if user hasn't edited locally.
                    if (!isLocalChanged)
                    {
                        _cacheService.DeletePostFromCache(id);
                        report.DeletedFromLocal.Add(id);
                    }
                    // If there are local edits, keep local to avoid data loss; user can resolve manually.
                }
            }
        }

        return report;
    }

    public async Task<SyncReport> SynchronizeMediaAsync(int syncLimit, CancellationToken cancellationToken)
    {
        var report = new SyncReport();

        // 1. Push local metadata changes
        var localMediaItems = _cacheService.ReadLocalMediaMetadata();
        foreach (var (mediaId, metadata) in localMediaItems)
        {
            var yamlContent = SerializeToYaml(metadata);
            var currentHash = _cacheService.ComputeSha256Hash(yamlContent);
            var previousHash = _cacheService.GetMediaMetadataHash(mediaId);

            if (previousHash != null && currentHash != previousHash)
            {
                var request = new WordPressUpdateMediaRequest
                {
                    Title = metadata.Title,
                    Description = metadata.Description,
                    Caption = metadata.Caption,
                    AltText = metadata.AltText
                };
                await _wpService.UpdateMediaAsync(mediaId, request, cancellationToken);
                report.PushedMediaToServer.Add(mediaId);
            }
        }

        // 2. Pull top-N media from server
        var allMedia = await _wpService.ListMediaAsync(perPage: syncLimit, page: 1, cancellationToken);
        foreach (var media in allMedia)
        {
            if (string.IsNullOrEmpty(media.SourceUrl)) continue;

            try
            {
                var fileContent = await _wpService.DownloadMediaFileAsync(media.SourceUrl, cancellationToken);
                _cacheService.SaveMediaToCache(media, fileContent);
                report.NewlyCachedMedia.Add(media.Id);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to sync media item {media.Id}: {ex.Message}");
                report.MediaConflicts.Add(media.Id);
            }
        }

        // 3. For locally present media not included in top-N, if local metadata is unmodified
        //    and server returns 404 (deleted), remove local cache.
        try
        {
            var serverIds = new HashSet<int>(allMedia.Select(m => m.Id));

            var localMediaMetas = _cacheService.ReadLocalMediaMetadata();
            foreach (var (mediaId, metadata) in localMediaMetas)
            {
                if (serverIds.Contains(mediaId)) continue; // within top-N, handled above

                var yamlContent = SerializeToYaml(metadata);
                var currentHash = _cacheService.ComputeSha256Hash(yamlContent);
                var previousHash = _cacheService.GetMediaMetadataHash(mediaId);

                var isUnmodified = previousHash != null && previousHash == currentHash;
                if (!isUnmodified) continue; // keep locally if edited

                try
                {
                    // Probe server existence
                    var _ = await _wpService.GetMediaAsync(mediaId, cancellationToken);
                }
                catch (WordPressApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    _cacheService.DeleteMediaFromCache(mediaId);
                    report.DeletedMediaFromLocal.Add(mediaId);
                }
            }
        }
        catch
        {
            // Best-effort cleanup; ignore failures and continue.
        }

        return report;
    }

    private string SerializeToYaml(EditableMediaMetadata data)
    {
        return YamlSerializer.Serialize(data);
    }

    private async Task SynchronizeLocalTaxonomyChangesAsync(SyncReport report, CancellationToken cancellationToken)
    {
        // 1. Read local taxonomy files
        var (localCategories, localTags) = _cacheService.ReadLocalTaxonomies();

        // 2. Synchronize Categories
        var (cachedCategories, cachedTags) = _cacheService.GetTaxonomies();
        var cachedCategoriesDict = cachedCategories.ToDictionary(c => c.Id);

        foreach (var localCat in localCategories)
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
                var editableCategoryForHash = new EditableCategory { Id = localCat.Id, Name = localCat.Name, Slug = localCat.Slug, Description = localCat.Description };
                var yamlContent = YamlSerializer.Serialize(editableCategoryForHash);
                var currentHash = _cacheService.ComputeSha256Hash(yamlContent);
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
        foreach (var localTag in localTags)
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
                var editableTagForHash = new EditableTag { Id = localTag.Id, Name = localTag.Name, Slug = localTag.Slug, Description = localTag.Description };
                var yamlContent = YamlSerializer.Serialize(editableTagForHash);
                var currentHash = _cacheService.ComputeSha256Hash(yamlContent);
                var previousHash = _cacheService.GetState($"tag_{localTag.Id}_hash");

                if (currentHash != previousHash)
                {
                    await _wpService.UpdateTagAsync(localTag.Id, new WordPressUpdateTagRequest { Name = localTag.Name, Slug = localTag.Slug, Description = localTag.Description }, cancellationToken);
                    report.PushedTaxonomies.Add($"Updated Tag: {localTag.Name}");
                }
            }
        }
    }

    private async Task CompareAndSyncAsync(int id, CachePostMetadata localMeta, WordPressPostDetail remotePost, ConnectionProfile profile, SyncReport report, CancellationToken cancellationToken)
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
                request.Date = localEditableMeta.Date;
                request.Excerpt = localEditableMeta.Excerpt;
                request.FeaturedMedia = localEditableMeta.FeaturedMedia;
                request.CommentStatus = localEditableMeta.CommentStatus;
                request.PingStatus = localEditableMeta.PingStatus;

                if (!TryResolveTaxonomyIds(localEditableMeta.Categories, _cacheService.FindCategoryId, out var categoryIds) ||
                    !TryResolveTaxonomyIds(localEditableMeta.Tags, _cacheService.FindTagId, out var tagIds))
                {
                    report.ConflictDetected.Add(id);
                    return;
                }
                request.Categories = categoryIds;
                request.Tags = tagIds;

                var updatedPost = await _wpService.UpdatePostAsync(id, request, cancellationToken);
                _cacheService.SavePostToCache(updatedPost);
                report.PushedToServer.Add(id);
            }
            else if (isServerChanged)
            {
                _cacheService.SavePostToCache(remotePost);
                report.PulledFromServer.Add(id);
            }
        }
    }

    private bool TryResolveTaxonomyIds(List<string>? namesOrIds, Func<string, int?> findId, out int[]? resolvedIds)
    {
        resolvedIds = null;
        if (namesOrIds == null) return true;

        var idList = new List<int>();
        foreach (var item in namesOrIds)
        {
            if (int.TryParse(item, out var id))
            {
                idList.Add(id);
            }
            else
            {
                var foundId = findId(item);
                if (foundId.HasValue)
                {
                    idList.Add(foundId.Value);
                }
                else
                {
                    // Could not resolve term, so we fail
                    Console.Error.WriteLine($"Error: Could not resolve taxonomy term '{item}' to an ID.");
                    resolvedIds = null;
                    return false;
                }
            }
        }

        resolvedIds = idList.ToArray();
        return true;
    }

    public async Task ResolveConflictAsync(string type, int id, string strategy, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        switch (type.ToLowerInvariant())
        {
            case "post":
                await ResolvePostConflictAsync(id, strategy, profile, cancellationToken);
                break;
            case "category":
            case "tag":
                // TODO: Implement taxonomy conflict resolution
                await ResolveTaxonomyConflictAsync(type, id, strategy, cancellationToken);
                break;
            default:
                throw new ArgumentException($"Unsupported conflict type: {type}");
        }
    }

    private async Task ResolvePostConflictAsync(int id, string strategy, ConnectionProfile profile, CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        Console.WriteLine($"Resolving conflict for post {id} with strategy: {strategy}...");

        if (strategy == "server-wins")
        {
            var remotePost = await _wpService.GetPostAsync(id, cancellationToken);
            _cacheService.SavePostToCache(remotePost);
            Console.WriteLine($"Conflict resolved. Local post {id} was overwritten with the server version.");
        }
        else if (strategy == "local-wins")
        {
            var localPost = _cacheService.ReadLocalPost(id)
                ?? throw new InvalidOperationException($"Could not read local post data for {id}. Cannot push local changes.");

            var request = new WordPressUpdatePostRequest();
            var meta = localPost.Metadata;

            // Initialize Meta from local file, then add/overwrite internal fields
            var metaForRequest = meta.Meta ?? new Dictionary<string, object?>();

            // Handle content based on edit mode
            var editMode = meta.EditMode ?? "html";
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

            request.Title = meta.Title;
            request.Slug = meta.Slug;
            request.Status = meta.Status;
            request.Date = meta.Date;
            request.Excerpt = meta.Excerpt;
            request.FeaturedMedia = meta.FeaturedMedia;
            request.CommentStatus = meta.CommentStatus;
            request.PingStatus = meta.PingStatus;

            if (!TryResolveTaxonomyIds(meta.Categories, _cacheService.FindCategoryId, out var categoryIds) ||
                !TryResolveTaxonomyIds(meta.Tags, _cacheService.FindTagId, out var tagIds))
            {
                throw new InvalidOperationException($"Failed to resolve taxonomy IDs for post {id}. Please check the category and tag names in the local file.");
            }
            request.Categories = categoryIds;
            request.Tags = tagIds;

            var updatedPost = await _wpService.UpdatePostAsync(id, request, cancellationToken);
            _cacheService.SavePostToCache(updatedPost);
            Console.WriteLine($"Conflict resolved. Server post {id} was overwritten with the local version.");
        }
    }

    private async Task ResolveTaxonomyConflictAsync(string type, int id, string strategy, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Resolving conflict for {type} {id} with strategy: {strategy}...");

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
            Console.WriteLine($"Conflict resolved. Local {type} {id} was overwritten with the server version.");
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
            Console.WriteLine($"Conflict resolved. Server {type} {id} was overwritten with the local version.");
        }
    }
}
