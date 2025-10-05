using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WpAiCli.WordPress;
using WpAiCli.WordPress.Models;

namespace WpAiCli.Services;

public class SyncReport
{
    public List<int> PushedToServer { get; } = new();
    public List<int> PulledFromServer { get; } = new();
    public List<int> DeletedFromLocal { get; } = new();
    public List<int> ConflictDetected { get; } = new();
    public List<int> NewlyCached { get; } = new();
    public List<string> PushedTaxonomies { get; } = new();
}

public class SyncService
{
    private readonly WordPressService _wpService;
    private readonly CacheService _cacheService;

    public SyncService(WordPressService wpService, CacheService cacheService)
    {
        _wpService = wpService;
        _cacheService = cacheService;
    }

    public async Task<SyncReport> SynchronizePostsAsync(string cachePath, int syncLimit, CancellationToken cancellationToken)
    {
        var report = new SyncReport();

        // 1. Push local taxonomy changes first
        await SynchronizeLocalTaxonomyChangesAsync(cachePath, report, cancellationToken);

        // 2. Synchronize taxonomies from server (pull changes and update local state)
        var allCategories = await _wpService.ListCategoriesAsync(cancellationToken);
        var allTags = await _wpService.ListTagsAsync(cancellationToken);
        await _cacheService.UpdateTaxonomiesCacheAsync(cachePath, allCategories, allTags);

        // 3. Synchronize posts
        var localPosts = _cacheService.ListLocalPostMetadata(cachePath)
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
                await CompareAndSyncAsync(id, localMeta, remotePostFromTopN, cachePath, report, cancellationToken);
            }
            else if (!hasLocal && hasRemoteInTopN)
            {
                _cacheService.SavePostToCache(remotePostFromTopN, cachePath);
                report.NewlyCached.Add(id);
            }
            else if (hasLocal && !hasRemoteInTopN)
            {
                var localContent = _cacheService.ReadLocalContent(id, cachePath);
                var localContentHash = _cacheService.ComputeSha256Hash(localContent);
                
                var localEditableMeta = _cacheService.ReadEditableMetadata(id, cachePath);
                var localEditableMetaYaml = _cacheService.SerializeToYaml(localEditableMeta ?? new EditablePostMetadata());
                var localEditableMetaHash = _cacheService.ComputeSha256Hash(localEditableMetaYaml);

                if (localContentHash != localMeta.ContentHash || localEditableMetaHash != localMeta.EditableMetaHash)
                {
                    try
                    {
                        var remotePost = await _wpService.GetPostAsync(id, cancellationToken);
                        await CompareAndSyncAsync(id, localMeta, remotePost, cachePath, report, cancellationToken);
                    }
                    catch (WordPressApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        _cacheService.DeletePostFromCache(id, cachePath);
                        report.DeletedFromLocal.Add(id);
                    }
                }
            }
        }

        return report;
    }

    private async Task SynchronizeLocalTaxonomyChangesAsync(string cachePath, SyncReport report, CancellationToken cancellationToken)
    {
        // Categories
        var categoriesPath = Path.Combine(cachePath, "categories.yaml");
        if (File.Exists(categoriesPath))
        {
            var yamlContent = await File.ReadAllTextAsync(categoriesPath, cancellationToken);
            var currentHash = _cacheService.ComputeSha256Hash(yamlContent);
            var previousHash = _cacheService.GetState("categories_yaml_hash");

            if (currentHash != previousHash)
            {
                var localCategories = _cacheService.DeserializeFromYaml<List<EditableCategory>>(yamlContent);
                var (cachedCategories, _) = _cacheService.GetTaxonomies();
                var cachedCategoriesDict = cachedCategories.ToDictionary(c => c.Id);

                foreach (var localCat in localCategories)
                {
                    if (cachedCategoriesDict.TryGetValue(localCat.Id, out var cachedCat) && 
                        (localCat.Name != cachedCat.Name || localCat.Slug != cachedCat.Slug))
                    {
                        await _wpService.UpdateCategoryAsync(localCat.Id, new WordPressUpdateCategoryRequest { Name = localCat.Name, Slug = localCat.Slug }, cancellationToken);
                        report.PushedTaxonomies.Add($"Category: {localCat.Name}");
                    }
                }
            }
        }

        // Tags
        var tagsPath = Path.Combine(cachePath, "tags.yaml");
        if (File.Exists(tagsPath))
        {
            var yamlContent = await File.ReadAllTextAsync(tagsPath, cancellationToken);
            var currentHash = _cacheService.ComputeSha256Hash(yamlContent);
            var previousHash = _cacheService.GetState("tags_yaml_hash");

            if (currentHash != previousHash)
            {
                var localTags = _cacheService.DeserializeFromYaml<List<EditableTag>>(yamlContent);
                var (_, cachedTags) = _cacheService.GetTaxonomies();
                var cachedTagsDict = cachedTags.ToDictionary(t => t.Id);

                foreach (var localTag in localTags)
                {
                    if (cachedTagsDict.TryGetValue(localTag.Id, out var cachedTag) && 
                        (localTag.Name != cachedTag.Name || localTag.Slug != cachedTag.Slug))
                    {
                        await _wpService.UpdateTagAsync(localTag.Id, new WordPressUpdateTagRequest { Name = localTag.Name, Slug = localTag.Slug }, cancellationToken);
                        report.PushedTaxonomies.Add($"Tag: {localTag.Name}");
                    }
                }
            }
        }
    }

    private async Task CompareAndSyncAsync(int id, CachePostMetadata localMeta, WordPressPostDetail remotePost, string cachePath, SyncReport report, CancellationToken cancellationToken)
    {
        var (contentFileExists, editableFileExists) = _cacheService.CheckCacheFileExistence(id, cachePath);

        if (!contentFileExists || !editableFileExists)
        {
            // Handle incomplete cache files
            var isLocalContentChanged = false;
            if (contentFileExists)
            {
                var localContent = _cacheService.ReadLocalContent(id, cachePath);
                var localContentHash = _cacheService.ComputeSha256Hash(localContent);
                isLocalContentChanged = localContentHash != localMeta.ContentHash;
            }

            var isLocalMetaChanged = false;
            if (editableFileExists)
            {
                var localEditableMeta = _cacheService.ReadEditableMetadata(id, cachePath);
                var localEditableMetaYaml = _cacheService.SerializeToYaml(localEditableMeta ?? new EditablePostMetadata());
                var localEditableMetaHash = _cacheService.ComputeSha256Hash(localEditableMetaYaml);
                isLocalMetaChanged = localEditableMetaHash != localMeta.EditableMetaHash;
            }

            if (isLocalContentChanged || isLocalMetaChanged)
            {
                // Incomplete cache with local modifications is a conflict
                report.ConflictDetected.Add(id);
            }
            else
            {
                // Incomplete cache but no local modifications, restore from server
                _cacheService.SavePostToCache(remotePost, cachePath);
                report.PulledFromServer.Add(id);
            }
        }
        else
        {
            // --- Both files exist, proceed with full comparison ---

            // Compare content hashes
            var localContent = _cacheService.ReadLocalContent(id, cachePath);
            var localContentHash = _cacheService.ComputeSha256Hash(localContent);
            var serverContentHash = _cacheService.ComputeSha256Hash(remotePost.Content?.Raw ?? string.Empty);
            var isLocalContentChanged = localContentHash != localMeta.ContentHash;
            var isServerContentChanged = serverContentHash != localMeta.ContentHash;

            // Compare editable meta hashes
            var localEditableMeta = _cacheService.ReadEditableMetadata(id, cachePath);
            var localEditableMetaYaml = _cacheService.SerializeToYaml(localEditableMeta ?? new EditablePostMetadata());
            var localEditableMetaHash = _cacheService.ComputeSha256Hash(localEditableMetaYaml);

            var serverEditableMeta = new EditablePostMetadata
            {
                Title = remotePost.Title?.Raw,
                Slug = remotePost.Slug,
                Status = remotePost.Status,
                Date = remotePost.Date,
                Excerpt = remotePost.Excerpt?.Raw,
                FeaturedMedia = remotePost.FeaturedMedia,
                CommentStatus = remotePost.CommentStatus,
                PingStatus = remotePost.PingStatus,
                Categories = remotePost.Categories?.Select(c => c.ToString()).ToList(),
                Tags = remotePost.Tags?.Select(t => t.ToString()).ToList()
            };
            var serverEditableMetaYaml = _cacheService.SerializeToYaml(serverEditableMeta);
            var serverEditableMetaHash = _cacheService.ComputeSha256Hash(serverEditableMetaYaml);

            var isLocalMetaChanged = localEditableMetaHash != localMeta.EditableMetaHash;
            var isServerMetaChanged = serverEditableMetaHash != localMeta.EditableMetaHash;

            if ((isLocalContentChanged && isServerContentChanged) || (isLocalMetaChanged && isServerMetaChanged))
            {
                report.ConflictDetected.Add(id);
            }
            else if (isLocalContentChanged || isLocalMetaChanged)
            {
                var request = new WordPressUpdatePostRequest();
                if (isLocalContentChanged) request.Content = localContent;
                if (isLocalMetaChanged && localEditableMeta != null)
                {
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
                }

                var updatedPost = await _wpService.UpdatePostAsync(id, request, cancellationToken);
                _cacheService.SavePostToCache(updatedPost, cachePath);
                report.PushedToServer.Add(id);
            }
            else if (isServerContentChanged || isServerMetaChanged)
            {
                _cacheService.SavePostToCache(remotePost, cachePath);
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
                    Console.Error.WriteLine($"Error: Could not resolve taxonomy term ''{item}'' to an ID.");
                    resolvedIds = null;
                    return false;
                }
            }
        }

        resolvedIds = idList.ToArray();
        return true;
    }
}