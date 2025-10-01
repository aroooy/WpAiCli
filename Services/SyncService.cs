using System;
using System.Collections.Generic;
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

        var localPosts = _cacheService.ListLocalPostMetadata(cachePath)
            .ToDictionary(meta => meta.Post.Id, meta => meta);

        // 2. Get TOP N remote posts by fetching each status separately as a workaround
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

    private async Task CompareAndSyncAsync(int id, CachePostMetadata localMeta, WordPressPostDetail remotePost, string cachePath, SyncReport report, CancellationToken cancellationToken)
    {
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
            PingStatus = remotePost.PingStatus
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