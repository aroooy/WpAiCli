using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using WpAiCli.Configuration;
using System.Linq;
using WpAiCli.Output;
using WpAiCli.Services;
using WpAiCli.WordPress.Models;
using Markdig;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

// Marks the class as a tool container
[McpServerToolType]
public static partial class WordPressMcpTools
{
    [McpServerTool]
    [Description("Displays a list of registered WordPress site connections.")]
    public static Task<string> ListConnections(
        IServiceProvider services
    )
    {
        var store = ConnectionStore.Load();
        if (store.Profiles.Count == 0)
        {
            return Task.FromResult("No connections have been registered yet.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("Registered connections:");
        for (int i = 0; i < store.Profiles.Count; i++)
        {
            var profile = store.Profiles[i];
            var isActive = string.Equals(profile.Name, store.ActiveConnection, StringComparison.OrdinalIgnoreCase);

            string prefix = isActive ? "=>" : "  ";
            
            sb.AppendLine($"{prefix} {i + 1}. {profile.Name} ({profile.BaseUrl})");
        }

        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(store.ActiveConnection))
        {
            sb.AppendLine($"=> indicates the active connection ({store.ActiveConnection}).");
        }

        return Task.FromResult(sb.ToString());
    }

    [McpServerTool]
    [Description("Switches the active connection by name.")]
    public static Task<string> SetActiveConnection(
        [Description("The name of the connection to set as active.")]
        string name,
        IServiceProvider services
    )
    {
        var store = ConnectionStore.Load();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult("Error: Connection name is required. Use ListConnections to see available options.");
        }

        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return Task.FromResult($"Error: Connection '{name}' not found.");
        }

        store.ActiveConnection = profile.Name;
        store.Save();

        return Task.FromResult($"Active connection set to '{profile.Name}'.");
    }

    [McpServerTool]
    [Description("Registers a new connection to a WordPress site.")]
    public static Task<string> AddConnection(
        [Description("An arbitrary name for the connection.")] string name,
        [Description("The base URL of the WordPress REST API.")] string baseUrl,
        [Description("Authentication method ('ApplicationPassword' or 'Jwt'). Defaults to 'ApplicationPassword'.")] string? authMethod,
        [Description("WordPress username for ApplicationPassword authentication.")] string? username,
        [Description("Application password for ApplicationPassword authentication.")] string? password,
        [Description("JWT token for Jwt authentication.")] string? jwtToken,
        [Description("Path to the local cache. Defaults to './wp-cache'.")] string? cachePath,
        [Description("The maximum number of items to fetch during synchronization.")] int? syncLimit,
        [Description("Markdown conversion mode ('client' or 'server').")] string? markdownConversion,
        IServiceProvider services
    )
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl))
        {
            return Task.FromResult("Error: --name and --base-url are required when adding a connection.");
        }

        var resolvedAuthMethod = authMethod ?? "ApplicationPassword";

        if (resolvedAuthMethod != "ApplicationPassword" && resolvedAuthMethod != "Jwt")
        {
            return Task.FromResult("Error: Invalid value for --auth-method. Must be 'ApplicationPassword' or 'Jwt'.");
        }
        
        string credential;
        string? resolvedUserName = null;

        if (resolvedAuthMethod == "ApplicationPassword")
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Task.FromResult("Error: For ApplicationPassword authentication, provide --username and --password.");
            }
            resolvedUserName = username;
            credential = password;
        }
        else // Jwt
        {
            if (string.IsNullOrWhiteSpace(jwtToken))
            {
                return Task.FromResult("Error: For Jwt authentication, provide --jwt-token.");
            }
            credential = jwtToken;
        }
        
        if (markdownConversion != null && markdownConversion != "client" && markdownConversion != "server")
        {
            return Task.FromResult("Error: Invalid value for --markdown-conversion. Must be 'client' or 'server'.");
        }

        var store = ConnectionStore.Load();
        bool isFirstConnection = store.Profiles.Count == 0;

        if (store.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult($"Error: Connection '{name}' already exists. Remove it first if you need to redefine it.");
        }
        
        var resolvedCachePath = cachePath;
        if (string.IsNullOrWhiteSpace(resolvedCachePath))
        {
            resolvedCachePath = Path.Combine(Directory.GetCurrentDirectory(), "wp-cache");
        }
        var absoluteCachePath = Path.GetFullPath(resolvedCachePath);

        var profile = new ConnectionProfile
        {
            Name = name,
            BaseUrl = baseUrl.Trim(),
            CredentialKey = $"WpAiCli/{name}",
            AuthMethod = resolvedAuthMethod,
            UserName = resolvedUserName,
            CachePath = absoluteCachePath,
            SyncItemsLimit = syncLimit,
            MarkdownConversion = markdownConversion
        };

        CredentialManager.Save(profile.CredentialKey, credential);
        store.Profiles.Add(profile);
        store.LastUsedConnection = profile.Name;

        if (isFirstConnection)
        {
            store.ActiveConnection = profile.Name;
        }

        store.Save();

        var sb = new StringBuilder();
        sb.AppendLine($"Connection '{profile.Name}' registered with '{profile.AuthMethod}' authentication.");

        if (isFirstConnection)
        {
            sb.AppendLine("As this is the first connection, it has been set as the active connection.");
        }
        
        return Task.FromResult(sb.ToString());
    }

    [McpServerTool]
    [Description("Updates an existing connection's settings.")]
    public static Task<string> UpdateConnection(
        [Description("The name of the connection to update.")] string name,
        [Description("The new base URL of the WordPress REST API.")] string? baseUrl,
        [Description("The new authentication method ('ApplicationPassword' or 'Jwt').")] string? authMethod,
        [Description("The new WordPress username for ApplicationPassword authentication.")] string? username,
        [Description("The new application password for ApplicationPassword authentication.")] string? password,
        [Description("The new JWT token for Jwt authentication.")] string? jwtToken,
        [Description("The new path to the local cache.")] string? cachePath,
        [Description("The new maximum number of items to fetch during synchronization.")] string? syncLimitStr,
        [Description("The new Markdown conversion mode ('client' or 'server').")] string? markdownConversion,
        IServiceProvider services
        )
    {
        var store = ConnectionStore.Load();
        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            return Task.FromResult($"Error: Connection '{name}' not found.");
        }

        var sb = new StringBuilder();
        var updated = false;

        if (baseUrl != null)
        {
            profile.BaseUrl = baseUrl.TrimEnd('/');
            updated = true;
            sb.AppendLine($"Base URL set to: {profile.BaseUrl}");
        }

        if (authMethod != null)
        {
            if (authMethod == "ApplicationPassword" || authMethod == "Jwt")
            {
                profile.AuthMethod = authMethod;
                updated = true;
                sb.AppendLine($"Authentication method set to: {profile.AuthMethod}");
            }
            else
            {
                return Task.FromResult("Error: Invalid auth-method. Must be 'ApplicationPassword' or 'Jwt'.");
            }
        }

        if (username != null)
        {
            profile.UserName = username;
            updated = true;
            sb.AppendLine($"Username set to: {profile.UserName}");
        }

        if(password != null)
        {
            CredentialManager.Save(profile.CredentialKey, password);
            updated = true;
            sb.AppendLine("Password updated.");
        }

        if(jwtToken != null)
        {
            CredentialManager.Save(profile.CredentialKey, jwtToken);
            updated = true;
            sb.AppendLine("JWT Token updated.");
        }

        if (cachePath is not null)
        {
            profile.CachePath = !string.IsNullOrWhiteSpace(cachePath) ? Path.GetFullPath(cachePath) : null;
            updated = true;
            sb.AppendLine(profile.CachePath is not null
                ? $"Cache location set to: {profile.CachePath}"
                : "Cache location has been removed.");
        }

        if (syncLimitStr is not null)
        {
            if (int.TryParse(syncLimitStr, out var parsedLimit) && parsedLimit > 0)
            {
                profile.SyncItemsLimit = parsedLimit;
                sb.AppendLine($"Sync limit set to: {profile.SyncItemsLimit}");
            }
            else if (string.IsNullOrEmpty(syncLimitStr))
            {
                profile.SyncItemsLimit = null;
                sb.AppendLine("Sync limit has been reset to default.");
            }
            else
            {
                return Task.FromResult($"Error: Invalid value for --sync-limit: '{syncLimitStr}'. Must be a positive integer.");
            }
            updated = true;
        }

        if (markdownConversion is not null)
        {
            if (markdownConversion == "client" || markdownConversion == "server")
            {
                profile.MarkdownConversion = markdownConversion;
                sb.AppendLine($"Markdown conversion strategy set to: {profile.MarkdownConversion}");
            }
            else if (string.IsNullOrEmpty(markdownConversion))
            {
                profile.MarkdownConversion = null; // Reset to default
                sb.AppendLine("Markdown conversion strategy has been reset to default.");
            }
            else
            {
                return Task.FromResult($"Error: Invalid value for --markdown-conversion: '{markdownConversion}'. Must be 'client' or 'server'.");
            }
            updated = true;
        }

        if (updated)
        {
            store.Save();
            sb.AppendLine($"\nConnection '{profile.Name}' updated.");
        }
        else
        {
            sb.AppendLine("No changes were made.");
        }

        return Task.FromResult(sb.ToString());
    }

    [McpServerTool]
    [Description("Deletes a registered connection by name.")]
    public static Task<string> RemoveConnection(
        [Description("The name of the connection to delete.")]
        string name,
        IServiceProvider services
    )
    {
        var store = ConnectionStore.Load();
        if (store.Profiles.Count == 0)
        {
            return Task.FromResult("No connections available to remove.");
        }

        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return Task.FromResult($"Error: Connection '{name}' not found.");
        }

        var profileName = profile.Name;
        int profileIndex = store.Profiles.IndexOf(profile);

        CredentialManager.Delete(profile.CredentialKey);
        store.Profiles.RemoveAt(profileIndex);
        
        if (string.Equals(store.LastUsedConnection, profileName, StringComparison.OrdinalIgnoreCase))
        {
            store.LastUsedConnection = store.Profiles.FirstOrDefault()?.Name;
        }

        store.Save();
        return Task.FromResult($"Connection '{profileName}' removed.");
    }

    [McpServerTool]
    [Description("Displays the path to the cache directory for the currently active connection.")]
    public static Task<string> ShowCachePath(
        IServiceProvider services
    )
    {
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        
        if (profile is null)
        {
            return Task.FromResult("Error: No active connection. Use 'connections active' to set one.");
        }

        if (string.IsNullOrWhiteSpace(profile.CachePath) || string.IsNullOrWhiteSpace(profile.Name))
        {
            return Task.FromResult("Error: Cache path is not configured for the active connection.");
        }

        var cacheRoot = Path.Combine(profile.CachePath!, profile.Name);
        return Task.FromResult(cacheRoot);
    }

    // --- Sync/Fetch Group ---

    [McpServerTool]
    [Description("Performs a two-way synchronization for posts and taxonomies (categories and tags).")]
    public static async Task<string> SyncPosts(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<SyncService>();
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        if (profile is null)
        {
            return "Error: No active connection.";
        }
        
        var sb = new StringBuilder();
        sb.AppendLine("Starting posts synchronization...");
        var syncLimit = profile.SyncItemsLimit ?? 30;
        var report = await syncService.SynchronizePostsAsync(profile, syncLimit, CancellationToken.None);
        sb.Append(FormatSyncReport(report));
        return sb.ToString();
    }

    [McpServerTool]
    [Description("Performs a two-way synchronization for categories and tags.")]
    public static async Task<string> SyncTaxonomies(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<SyncService>();

        var sb = new StringBuilder();
        sb.AppendLine("Starting taxonomies synchronization...");
        var report = await syncService.SynchronizeTaxonomiesAsync(CancellationToken.None);
        sb.Append(FormatSyncReport(report));
        return sb.ToString();
    }

    [McpServerTool]
    [Description("Performs a synchronization for the media library.")]
    public static async Task<string> SyncMedia(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<SyncService>();
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        if (profile is null)
        {
            return "Error: No active connection.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Starting media synchronization...");
        var syncLimit = profile.SyncItemsLimit ?? 30;
        var report = await syncService.SynchronizeMediaAsync(syncLimit, CancellationToken.None);
        sb.Append(FormatSyncReport(report));
        return sb.ToString();
    }

    [McpServerTool]
    [Description("Downloads all revisions (history) for a specified post and saves them to the local cache.")]
    public static async Task<string> FetchRevisions(
        [Description("The ID of the post for which to fetch revisions.")] int postId,
        IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var wpService = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();
        
        var sb = new StringBuilder();
        var revisions = await wpService.GetPostRevisionsAsync(postId, CancellationToken.None);
        if (revisions == null || !revisions.Any())
        {
            sb.AppendLine($"No revisions found for post {postId}.");
            return sb.ToString();
        }

        sb.AppendLine($"Fetching {revisions.Count()} revisions for post {postId}...");
        foreach (var revisionSummary in revisions)
        {
            sb.AppendLine($"  -> Fetching revision {revisionSummary.Id}...");
            var fullRevision = await wpService.GetPostRevisionAsync(postId, revisionSummary.Id, CancellationToken.None);
            cacheService.SaveRevisionToCache(fullRevision);
        }

        sb.AppendLine($"\nFetch complete. Revisions are saved in: wp-cache/revisions/post_{postId}/");
        return sb.ToString();
    }

    private static string FormatSyncReport(SyncReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n--- Sync Report ---");
        sb.AppendLine($"Pushed to server: {report.PushedToServer.Count} post(s)");
        sb.AppendLine($"Pulled from server: {report.PulledFromServer.Count} post(s)");
        sb.AppendLine($"Newly cached: {report.NewlyCached.Count} post(s)");
        sb.AppendLine($"Deleted from local: {report.DeletedFromLocal.Count} post(s)");
        sb.AppendLine($"Local validation errors (skipped): {report.LocalValidationErrors.Count} post(s)");
        sb.AppendLine($"Conflicts detected (skipped): {report.ConflictDetected.Count} post(s)");
        if (report.PushedTaxonomies.Count > 0)
        {
            sb.AppendLine($"Pushed taxonomies: {report.PushedTaxonomies.Count}");
        }
        if (report.PushedMediaToServer.Count > 0 || report.NewlyCachedMedia.Count > 0 || report.MediaConflicts.Count > 0 || report.DeletedMediaFromLocal.Count > 0 || report.PulledMediaFromServer.Count > 0)
        {
            sb.AppendLine("--- Media ---");
            sb.AppendLine($"Pushed metadata to server: {report.PushedMediaToServer.Count} item(s)");
            sb.AppendLine($"Pulled from server: {report.PulledMediaFromServer.Count} item(s)");
            sb.AppendLine($"Newly cached from server: {report.NewlyCachedMedia.Count} item(s)");
            sb.AppendLine($"Deleted from local: {report.DeletedMediaFromLocal.Count} item(s)");
            sb.AppendLine($"Conflicts/Errors: {report.MediaConflicts.Count} item(s)");
        }

        if (report.LocalValidationErrors.Count > 0)
        {
            sb.AppendLine("\nLocal validation errors detected:");
            foreach (var (postId, errorMessage) in report.LocalValidationErrors)
            {
                sb.AppendLine($"- {errorMessage}");
            }
        }

        if (report.ConflictDetected.Count > 0)
        {
            sb.AppendLine("\nConflicts detected for the following Post IDs:");
            sb.AppendLine($"  {string.Join(", ", report.ConflictDetected)}");
            sb.AppendLine("Please resolve them individually using the 'resolve' command.");
            sb.AppendLine("Example: wpai resolve post 123 --strategy [local-wins|server-wins]");
        }
        sb.AppendLine("-------------------");
        return sb.ToString();
    }

    // --- Create/Edit Group ---

    [McpServerTool]
    [Description("Creates a new post.")]
    public static async Task<string> CreatePost(
        [Description("The title of the post.")] string title,
        [Description("The content of the post body.")] string? content,
        [Description("Path to a file containing the content for the body.")] string? contentFile,
        [Description("The publication status (e.g., 'publish', 'draft'). Defaults to 'draft'.")] string? status,
        [Description("The editing mode ('markdown' or 'html'). Defaults to 'markdown'.")] string? editMode,
        [Description("An array of category IDs.")] int[]? categories,
        [Description("An array of tag IDs.")] int[]? tags,
        [Description("The ID of the featured image.")] int? featuredMedia,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        if (profile is null)
        {
            return "Error: No active connection.";
        }

        if (string.IsNullOrWhiteSpace(title)) return "Error: --title is required and cannot be empty.";

        var bodyContent = content;
        if (string.IsNullOrWhiteSpace(bodyContent) && !string.IsNullOrWhiteSpace(contentFile) && File.Exists(contentFile))
        {
            bodyContent = await File.ReadAllTextAsync(contentFile);
        }

        if (string.IsNullOrWhiteSpace(bodyContent)) return "Error: Either content or a valid contentFile is required.";

        var resolvedStatus = status ?? "draft";
        var resolvedEditMode = editMode ?? "markdown";
        if (resolvedEditMode != "markdown" && resolvedEditMode != "html") return "Error: Invalid value for --edit-mode. Must be 'markdown' or 'html'.";

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
            request.Content = conversion == "client" ? Markdig.Markdown.ToHtml(bodyContent ?? string.Empty) : bodyContent;
        }
        else
        {
            request.Content = bodyContent;
        }

        var post = await service.CreatePostAsync(request, CancellationToken.None);
        if (post != null)
        {
            if (!string.IsNullOrEmpty(profile.CachePath))
            {
                cacheService.SavePostToCache(post);
            }
            return OutputFormatter.FormatPost(post, OutputFormat.Table);
        }
        return "Error: Failed to create post.";
    }

    [McpServerTool]
    [Description("Creates a new category.")]
    public static async Task<string> CreateCategory(
        [Description("The name of the category.")] string name,
        [Description("The slug for the category.")] string? slug,
        [Description("A description for the category.")] string? description,
        IServiceProvider services
    )
    {
        if (string.IsNullOrWhiteSpace(name)) return "Error: --name is required.";
        
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();

        var request = new WordPressCreateCategoryRequest { Name = name, Slug = slug, Description = description };
        var category = await service.CreateCategoryAsync(request, CancellationToken.None);
        
        try { cacheService.SaveCategoryToCache(category); } catch { /* Ignore cache errors */ }
        
        return OutputFormatter.FormatCategory(category, OutputFormat.Table);
    }

    [McpServerTool]
    [Description("Creates a new tag.")]
    public static async Task<string> CreateTag(
        [Description("The name of the tag.")] string name,
        [Description("The slug for the tag.")] string? slug,
        [Description("A description for the tag.")] string? description,
        IServiceProvider services
    )
    {
        if (string.IsNullOrWhiteSpace(name)) return "Error: --name is required.";

        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();

        var request = new WordPressCreateTagRequest { Name = name, Slug = slug, Description = description };
        var tag = await service.CreateTagAsync(request, CancellationToken.None);

        try { cacheService.SaveTagToCache(tag); } catch { /* Ignore cache errors */ }

        return OutputFormatter.FormatTag(tag, OutputFormat.Table);
    }

    [McpServerTool]
    [Description("Uploads a media file.")]
    public static async Task<string> UploadMedia(
        [Description("Path to the file to upload.")] string filePath,
        [Description("The title for the media item.")] string? title,
        [Description("The description for the media item.")] string? description,
        IServiceProvider services
    )
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return "Error: A valid file path is required.";

        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();

        var resolvedTitle = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(filePath) : title;

        var mediaItem = await service.UploadMediaAsync(filePath, resolvedTitle, description, CancellationToken.None);
        try
        {
            if (!string.IsNullOrEmpty(mediaItem.SourceUrl))
            {
                var bytes = await service.DownloadMediaFileAsync(mediaItem.SourceUrl!, CancellationToken.None);
                cacheService.SaveMediaToCache(mediaItem, bytes);
            }
        }
        catch { /* Ignore cache errors */ }
        
        return OutputFormatter.FormatMediaItem(mediaItem, OutputFormat.Table);
    }

    [McpServerTool]
    [Description("Resolves a synchronization conflict.")]
    public static async Task<string> ResolveConflict(
        [Description("The type of content that conflicted ('post', 'category', 'tag').")] string type,
        [Description("The ID of the conflicted item.")] int id,
        [Description("The resolution strategy ('local-wins' or 'server-wins').")] string strategy,
        IServiceProvider services
    )
    {
        if (strategy != "local-wins" && strategy != "server-wins")
        {
            return "Error: Invalid strategy. Must be one of: local-wins, server-wins";
        }

        using var scope = services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<SyncService>();
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        if (profile is null)
        {
            return "Error: No active connection.";
        }

        return await syncService.ResolveConflictAsync(type, id, strategy, profile, CancellationToken.None);
    }

    // --- Delete/Organize Group ---

    [McpServerTool]
    [Description("Deletes a post.")]
    public static async Task<string> DeletePost(
        [Description("The ID of the post to delete.")] int id,
        [Description("Completely delete, bypassing the trash.")] bool force,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();

        try
        {
            var response = await service.DeletePostAsync(id, force, CancellationToken.None);
            if (response.Deleted)
            {
                cacheService.DeletePostFromCache(id);
            }
            return OutputFormatter.FormatDeleteResponse(response, OutputFormat.Table);
        }
        catch (WpAiCli.WordPress.WordPressApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cacheService.DeletePostFromCache(id);
            return OutputFormatter.FormatDeleteResponse(new WordPressDeleteResponse { Deleted = true, Previous = ToJsonElementDict(new { title = new { raw = "Unknown (already deleted)" } }) }, OutputFormat.Table);
        }
    }

    [McpServerTool]
    [Description("Deletes a category.")]
    public static async Task<string> DeleteCategory(
        [Description("The ID of the category to delete.")] int id,
        [Description("Delete completely.")] bool force,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();

        try
        {
            var response = await service.DeleteCategoryAsync(id, force, CancellationToken.None);
            if (response.Deleted)
            {
                cacheService.DeleteCategoryFromCache(id);
            }
            return OutputFormatter.FormatDeleteResponse(response, OutputFormat.Table);
        }
        catch (WpAiCli.WordPress.WordPressApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cacheService.DeleteCategoryFromCache(id);
            return OutputFormatter.FormatDeleteResponse(new WordPressDeleteResponse { Deleted = true, Previous = ToJsonElementDict(new { name = "Unknown (already deleted)" }) }, OutputFormat.Table);
        }
    }

    [McpServerTool]
    [Description("Deletes a tag.")]
    public static async Task<string> DeleteTag(
        [Description("The ID of the tag to delete.")] int id,
        [Description("Delete completely.")] bool force,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();

        try
        {
            var response = await service.DeleteTagAsync(id, force, CancellationToken.None);
            if (response.Deleted)
            {
                cacheService.DeleteTagFromCache(id);
            }
            return OutputFormatter.FormatDeleteResponse(response, OutputFormat.Table);
        }
        catch (WpAiCli.WordPress.WordPressApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cacheService.DeleteTagFromCache(id);
            return OutputFormatter.FormatDeleteResponse(new WordPressDeleteResponse { Deleted = true, Previous = ToJsonElementDict(new { name = "Unknown (already deleted)" }) }, OutputFormat.Table);
        }
    }

    [McpServerTool]
    [Description("Deletes a media item.")]
    public static async Task<string> DeleteMedia(
        [Description("The ID of the media item to delete.")] int id,
        [Description("Completely delete, bypassing the trash.")] bool force,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();
        try
        {
            var response = await service.DeleteMediaAsync(id, force, CancellationToken.None);
            if (response.Deleted)
            {
                cacheService.DeleteMediaFromCache(id);
            }
            return OutputFormatter.FormatDeleteResponse(response, OutputFormat.Table);
        }
        catch (WpAiCli.WordPress.WordPressApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cacheService.DeleteMediaFromCache(id);
            return OutputFormatter.FormatDeleteResponse(new WordPressDeleteResponse { Deleted = true, Previous = ToJsonElementDict(new { title = new { raw = "Unknown (already deleted)" } }) }, OutputFormat.Table);
        }
    }

    [McpServerTool]
    [Description("Deletes the local revision cache.")]
    public static Task<string> CleanRevisions(
        [Description("Specify a post ID to delete the cache for that post only.")] int? postId,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();
        cacheService.CleanRevisionsCache(postId);
        if (postId.HasValue)
        {
            return Task.FromResult($"Revision cache for post {postId.Value} cleaned.");
        }
        return Task.FromResult("All local revision caches cleaned.");
    }
    
    [McpServerTool]
    [Description("Organizes local post files into subfolders based on their status.")]
    public static Task<string> OrganizePosts(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();
        cacheService.OrganizePostFiles();
        return Task.FromResult("Local post files organized successfully.");
    }

    private static Dictionary<string, JsonElement>? ToJsonElementDict(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
    }
}
