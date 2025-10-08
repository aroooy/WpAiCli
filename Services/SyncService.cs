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
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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

    public async Task<SyncReport> SynchronizeMediaAsync(string cachePath, int syncLimit, CancellationToken cancellationToken)
    {
        var report = new SyncReport();

        // 1. Push local metadata changes
        var localMediaItems = _cacheService.ReadLocalMediaMetadata(cachePath);
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

        // 2. Pull all media from server
        var allMedia = await _wpService.ListMediaAsync(perPage: syncLimit, page: 1, cancellationToken);
        foreach (var media in allMedia)
        {
            if (string.IsNullOrEmpty(media.SourceUrl)) continue;

            try
            {
                var fileContent = await _wpService.DownloadMediaFileAsync(media.SourceUrl, cancellationToken);
                _cacheService.SaveMediaToCache(media, fileContent, cachePath);
                report.NewlyCachedMedia.Add(media.Id);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to sync media item {media.Id}: {ex.Message}");
                report.MediaConflicts.Add(media.Id);
            }
        }

        return report;
    }

    private string SerializeToYaml(EditableMediaMetadata data)
    {
        return YamlSerializer.Serialize(data);
    }

    private async Task SynchronizeLocalTaxonomyChangesAsync(string cachePath, SyncReport report, CancellationToken cancellationToken)
    {
        // 1. Read local taxonomy files
        var (localCategories, localTags) = _cacheService.ReadLocalTaxonomies(cachePath);

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