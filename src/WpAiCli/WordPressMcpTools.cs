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

    // --- Read/Inspect Group ---

    [McpServerTool]
    [Description("[PRIMARY] Lists posts from the WordPress site.")]
    public static async Task<string> ListPosts(
        [Description("Filter by post status (e.g. 'publish', 'draft', 'any'). Defaults to null (all).")] string? status,
        [Description("Number of posts per page (1-100). Defaults to 10.")] int? perPage,
        [Description("Page number. Defaults to 1.")] int? page,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var pPage = Math.Clamp(perPage ?? 10, 1, 100);
        var pNum = Math.Max(page ?? 1, 1);
        var posts = await service.ListPostsAsync(status, pPage, pNum, CancellationToken.None);
        return OutputFormatter.FormatPosts(posts, OutputFormat.Table);
    }

    [McpServerTool]
    [Description("[PRIMARY] Retrieves details of a specific post by ID, including its metadata, content, and the local cache file path.")]
    public static async Task<string> GetPost(
        [Description("The ID of the post to retrieve.")] int id,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();

        var post = await service.GetPostAsync(id, CancellationToken.None);
        var sb = new StringBuilder();
        sb.Append(OutputFormatter.FormatPost(post, OutputFormat.Table));

        var localFile = cacheService.FindFileByPattern($"{id}-*.md");
        if (!string.IsNullOrEmpty(localFile) && File.Exists(localFile))
        {
            sb.AppendLine();
            sb.AppendLine($"[Local Cache File]: {localFile}");
            sb.AppendLine("NOTE: To edit this post, modify the Markdown file above and run PushPost.");
        }

        return sb.ToString();
    }

    [McpServerTool]
    [Description("Lists categories registered on the WordPress site.")]
    public static async Task<string> ListCategories(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var categories = await service.ListCategoriesAsync(CancellationToken.None);
        return OutputFormatter.FormatCategories(categories, OutputFormat.Table);
    }

    [McpServerTool]
    [Description("Lists tags registered on the WordPress site.")]
    public static async Task<string> ListTags(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var tags = await service.ListTagsAsync(CancellationToken.None);
        return OutputFormatter.FormatTags(tags, OutputFormat.Table);
    }

    [McpServerTool]
    [Description("Lists items from the WordPress media library.")]
    public static async Task<string> ListMedia(
        [Description("Number of media items per page (1-100). Defaults to 10.")] int? perPage,
        [Description("Page number. Defaults to 1.")] int? page,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var pPage = Math.Clamp(perPage ?? 10, 1, 100);
        var pNum = Math.Max(page ?? 1, 1);
        var items = await service.ListMediaAsync(pPage, pNum, CancellationToken.None);
        return OutputFormatter.FormatMediaItems(items, OutputFormat.Table);
    }

    // --- Workspace Pull Group ---

    [McpServerTool]
    [Description("[PRIMARY] Pulls the latest posts and taxonomies from WordPress into the local cache. Safe: local modifications are preserved and never pushed automatically.")]
    public static async Task<string> PullPosts(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        if (profile is null)
        {
            return "Error: No active connection.";
        }
        
        var sb = new StringBuilder();
        sb.AppendLine("Starting posts pull...");
        var syncLimit = profile.SyncItemsLimit ?? 30;
        var report = await workspaceService.PullPostsAsync(profile, syncLimit, CancellationToken.None);
        sb.Append(OutputFormatter.FormatTransferReport(report));
        return sb.ToString();
    }

    [McpServerTool]
    [Description("Pulls categories and tags from WordPress into the local cache. Safe: local taxonomy edits are preserved and never pushed automatically.")]
    public static async Task<string> PullTaxonomies(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();

        var sb = new StringBuilder();
        sb.AppendLine("Starting taxonomies pull...");
        var report = await workspaceService.PullTaxonomiesAsync(CancellationToken.None);
        sb.Append(OutputFormatter.FormatTransferReport(report));
        return sb.ToString();
    }

    [McpServerTool]
    [Description("Pulls media library metadata and files from WordPress into the local cache. Safe: local metadata edits are preserved and never pushed automatically.")]
    public static async Task<string> PullMedia(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        if (profile is null)
        {
            return "Error: No active connection.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Starting media pull...");
        var syncLimit = profile.SyncItemsLimit ?? 30;
        var report = await workspaceService.PullMediaAsync(syncLimit, CancellationToken.None);
        sb.Append(OutputFormatter.FormatTransferReport(report));
        return sb.ToString();
    }

    [McpServerTool]
    [Description("[ADVANCED] Downloads all revisions (history) for a specified post to the local cache for manual inspection or comparison.")]
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

    // --- Create/Push/Edit Group ---

    [McpServerTool]
    [Description("[PRIMARY] Creates a new post on WordPress and saves it to the local cache under the appropriate status folder (e.g. 'posts/draft/'). Do NOT manually move or rename the generated cache file.")]
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
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        if (profile is null)
        {
            return "Error: No active connection.";
        }

        try
        {
            var (post, cacheResult) = await workspaceService.CreatePostAsync(
                title,
                content,
                contentFile,
                status,
                editMode,
                categories,
                tags,
                featuredMedia,
                profile,
                CancellationToken.None);

            var sb = new StringBuilder();
            if (cacheResult != null)
            {
                sb.AppendLine($"[Cache] Post created and saved to '{cacheResult.CurrentStatus}' folder ({Path.GetFileName(cacheResult.FilePath)}).");
                sb.AppendLine("NOTE: Do NOT move or rename cache files manually. Status folder changes are handled automatically by WpAiCli.");
                sb.AppendLine();
            }
            sb.Append(OutputFormatter.FormatPost(post, OutputFormat.Table));
            return sb.ToString();
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error: Failed to create post: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("[PRIMARY] Pushes local changes of a post (or all modified posts) from the cache to the WordPress server.")]
    public static async Task<string> PushPost(
        [Description("The ID of the specific post to push. Ignored if 'all' is true.")] int? id,
        [Description("Set to true to push all locally modified posts. If true, 'id' is ignored. Defaults to false.")] bool? all,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        if (profile is null)
        {
            return "Error: No active connection.";
        }

        var pushAll = all.GetValueOrDefault(false);
        if (pushAll)
        {
            var report = await workspaceService.PushAllModifiedPostsAsync(profile, CancellationToken.None);
            return OutputFormatter.FormatTransferReport(report);
        }
        else
        {
            if (!id.HasValue)
            {
                return "Error: Provide a post ID, or set 'all' to true to push all modified posts.";
            }

            var (updated, cacheResult) = await workspaceService.PushPostAsync(id.Value, profile, CancellationToken.None);
            var sb = new StringBuilder();
            if (cacheResult.WasMoved)
            {
                sb.AppendLine(cacheResult.MoveMessage);
                sb.AppendLine();
            }
            sb.Append(OutputFormatter.FormatPost(updated, OutputFormat.Table));
            return sb.ToString();
        }
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
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();

        try
        {
            var category = await workspaceService.CreateCategoryAsync(name, slug, description, CancellationToken.None);
            return OutputFormatter.FormatCategory(category, OutputFormat.Table);
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error: Failed to create category: {ex.Message}";
        }
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
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();

        try
        {
            var tag = await workspaceService.CreateTagAsync(name, slug, description, CancellationToken.None);
            return OutputFormatter.FormatTag(tag, OutputFormat.Table);
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error: Failed to create tag: {ex.Message}";
        }
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
    [Description("[ADVANCED] Resolves a transfer conflict when a post or taxonomy differs on both server and local. Use only when reported by PullPosts or PushPost.")]
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
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
        var store = ConnectionStore.Load();
        var profile = store.GetActiveProfile();
        if (profile is null)
        {
            return "Error: No active connection.";
        }

        return await workspaceService.ResolveConflictAsync(type, id, strategy, profile, CancellationToken.None);
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
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();

        try
        {
            var (response, cachedTitle, alreadyDeleted) = await workspaceService.DeletePostAsync(id, force, CancellationToken.None);
            var result = OutputFormatter.FormatDeleteResponse(response, OutputFormat.Table, id, cachedTitle);
            if (alreadyDeleted)
            {
                return $"Post {id} was already deleted on server. Local cache cleared.\n{result}".TrimEnd();
            }
            return $"Successfully deleted post {id}.\n{result}".TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error deleting post {id}: {ex.Message}";
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
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();

        try
        {
            var (response, cachedName, alreadyDeleted) = await workspaceService.DeleteCategoryAsync(id, force, CancellationToken.None);
            var result = OutputFormatter.FormatDeleteResponse(response, OutputFormat.Table, id, cachedName);
            if (alreadyDeleted)
            {
                return $"Category {id} was already deleted on server. Local cache cleared.\n{result}".TrimEnd();
            }
            return $"Successfully deleted category {id}.\n{result}".TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error deleting category {id}: {ex.Message}";
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
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();

        try
        {
            var (response, cachedName, alreadyDeleted) = await workspaceService.DeleteTagAsync(id, force, CancellationToken.None);
            var result = OutputFormatter.FormatDeleteResponse(response, OutputFormat.Table, id, cachedName);
            if (alreadyDeleted)
            {
                return $"Tag {id} was already deleted on server. Local cache cleared.\n{result}".TrimEnd();
            }
            return $"Successfully deleted tag {id}.\n{result}".TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error deleting tag {id}: {ex.Message}";
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
        var workspaceService = scope.ServiceProvider.GetRequiredService<WorkspaceService>();

        try
        {
            var (response, cachedTitle, alreadyDeleted) = await workspaceService.DeleteMediaAsync(id, force, CancellationToken.None);
            var result = OutputFormatter.FormatDeleteResponse(response, OutputFormat.Table, id, cachedTitle);
            if (alreadyDeleted)
            {
                return $"Media {id} was already deleted on server. Local cache cleared.\n{result}".TrimEnd();
            }
            return $"Successfully deleted media {id}.\n{result}".TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error deleting media {id}: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("[MAINTENANCE] Deletes the local revision cache for post revisions.")]
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
    [Description("[MAINTENANCE] Organizes local post files into status subfolders (e.g. 'draft', 'publish') based on their YAML front-matter status. NOTE: Normally unnecessary as PushPost and PullPosts do this automatically.")]
    public static Task<string> OrganizePosts(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();
        var moved = cacheService.OrganizePostFiles();
        if (moved.Count == 0)
        {
            return Task.FromResult("All local post files are already in their correct status folders. No files were moved.");
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Successfully organized {moved.Count} post file(s):");
        foreach (var item in moved)
        {
            sb.AppendLine($"- {item}");
        }
        return Task.FromResult(sb.ToString());
    }
}
