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

// クラス自体をツールコンテナとしてマーク
[McpServerToolType]
public static class WordPressMcpTools
{
    [McpServerTool]
    [Description("登録されているWordPressサイトへの接続の一覧を表示します。")]
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
    [Description("操作対象とする接続（アクティブな接続）を、名前または番号で切り替えます。")]
    public static Task<string> SetActiveConnection(
        [Description("アクティブに設定する接続の名前または1から始まる番号。")]
        string nameOrNumber,
        IServiceProvider services
    )
    {
        var store = ConnectionStore.Load();
        if (string.IsNullOrWhiteSpace(nameOrNumber))
        {
            return Task.FromResult("Error: Connection name or number is required. Use ListConnections to see available options.");
        }

        ConnectionProfile? profile = null;

        // Try to parse as number first
        if (int.TryParse(nameOrNumber, out var choice) && choice >= 1 && choice <= store.Profiles.Count)
        {
            profile = store.Profiles[choice - 1];
        }
        else
        {
            // Fallback to parsing as name
            profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, nameOrNumber, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                return Task.FromResult($"Error: Connection '{nameOrNumber}' not found.");
            }
        }

        store.ActiveConnection = profile.Name;
        store.Save();

        return Task.FromResult($"Active connection set to '{profile.Name}'.");
    }

    [McpServerTool]
    [Description("新しいWordPressサイトへの接続を登録します。")]
    public static Task<string> AddConnection(
        [Description("接続の任意の名前。")] string name,
        [Description("WordPress REST APIのベースURL。")] string baseUrl,
        [Description("認証方法 ('ApplicationPassword' または 'Jwt')。デフォルトは 'ApplicationPassword'。")] string? authMethod,
        [Description("ApplicationPassword認証用のWordPressユーザー名。")] string? username,
        [Description("ApplicationPassword認証用のアプリケーションパスワード。")] string? password,
        [Description("Jwt認証用のJWTトークン。")] string? jwtToken,
        [Description("ローカルキャッシュのパス。省略時は './wp-cache'。")] string? cachePath,
        [Description("同期間に取得するアイテム数の上限。")] int? syncLimit,
        [Description("Markdown変換モード ('client' または 'server')。")] string? markdownConversion,
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
    [Description("既存の接続設定を更新します。")]
    public static Task<string> UpdateConnection(
        [Description("更新する接続の名前。")] string name,
        [Description("新しいWordPress REST APIのベースURL。")] string? baseUrl,
        [Description("新しい認証方法 ('ApplicationPassword' または 'Jwt')。")] string? authMethod,
        [Description("新しいApplicationPassword認証用のWordPressユーザー名。")] string? username,
        [Description("新しいApplicationPassword認証用のアプリケーションパスワード。")] string? password,
        [Description("新しいJwt認証用のJWTトークン。")] string? jwtToken,
        [Description("新しいローカルキャッシュのパス。")] string? cachePath,
        [Description("新しい同期間に取得するアイテム数の上限。")] string? syncLimitStr,
        [Description("新しいMarkdown変換モード ('client' または 'server')。")] string? markdownConversion,
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
    [Description("登録済みの接続を、名前または番号で削除します。")]
    public static Task<string> RemoveConnection(
        [Description("削除する接続の名前または1から始まる番号。")]
        string nameOrNumber,
        IServiceProvider services
    )
    {
        var store = ConnectionStore.Load();
        if (store.Profiles.Count == 0)
        {
            return Task.FromResult("No connections available to remove.");
        }

        ConnectionProfile? profile = null;
        int profileIndex = -1;

        // Try to parse as number first
        if (int.TryParse(nameOrNumber, out var choice) && choice >= 1 && choice <= store.Profiles.Count)
        {
            profileIndex = choice - 1;
            profile = store.Profiles[profileIndex];
        }
        else
        {
            // Fallback to parsing as name
            profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, nameOrNumber, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                return Task.FromResult($"Error: Connection '{nameOrNumber}' not found.");
            }
            profileIndex = store.Profiles.IndexOf(profile);
        }

        var profileName = profile.Name;

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
    [Description("現在アクティブな接続のキャッシュディレクトリのパスを表示します。")]
    public static Task<string> ShowCachePath(
        IServiceProvider services
    )
    {
        var store = ConnectionStore.Load();
        var activeConnectionName = store.ActiveConnection;

        if (string.IsNullOrWhiteSpace(activeConnectionName))
        {
            return Task.FromResult("Error: No active connection. Use 'connections active' to set one.");
        }

        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, activeConnectionName, StringComparison.OrdinalIgnoreCase));
        
        if (profile is null)
        {
            return Task.FromResult($"Error: Active connection '{activeConnectionName}' not found in registered profiles.");
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
    [Description("投稿とタクソノミー（カテゴリ・タグ）の双方向同期を行います。")]
    public static async Task<string> SyncPosts(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<SyncService>();
        var store = ConnectionStore.Load();
        
        var activeConnectionName = store.ActiveConnection;
        if (string.IsNullOrWhiteSpace(activeConnectionName))
        {
            return "Error: No active connection.";
        }
        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, activeConnectionName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return $"Error: Active connection '{activeConnectionName}' not found.";
        }
        
        var sb = new StringBuilder();
        sb.AppendLine("Starting posts synchronization...");
        var syncLimit = profile.SyncItemsLimit ?? 30;
        var report = await syncService.SynchronizePostsAsync(profile, syncLimit, CancellationToken.None);
        sb.Append(FormatSyncReport(report));
        return sb.ToString();
    }

    [McpServerTool]
    [Description("カテゴリとタグの双方向同期を行います。")]
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
    [Description("メディアライブラリの同期を行います。")]
    public static async Task<string> SyncMedia(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<SyncService>();
        var store = ConnectionStore.Load();
        
        var activeConnectionName = store.ActiveConnection;
        if (string.IsNullOrWhiteSpace(activeConnectionName))
        {
            return "Error: No active connection.";
        }
        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, activeConnectionName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return $"Error: Active connection '{activeConnectionName}' not found.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Starting media synchronization...");
        var syncLimit = profile.SyncItemsLimit ?? 30;
        var report = await syncService.SynchronizeMediaAsync(syncLimit, CancellationToken.None);
        sb.Append(FormatSyncReport(report));
        return sb.ToString();
    }

    [McpServerTool]
    [Description("指定した投稿のリビジョン（履歴）を全てダウンロードし、ローカルキャッシュに保存します。")]
    public static async Task<string> FetchRevisions(
        [Description("リビジョンを取得する投稿のID。")] int postId,
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
    [Description("新しい投稿を作成します。")]
    public static async Task<string> CreatePost(
        [Description("投稿のタイトル。")] string title,
        [Description("投稿の本文。")] string? content,
        [Description("本文が含まれるファイルへのパス。")] string? contentFile,
        [Description("公開ステータス (例: 'publish', 'draft')。デフォルトは 'draft'。")] string? status,
        [Description("編集モード ('markdown' または 'html')。デフォルトは 'markdown'。")] string? editMode,
        [Description("カテゴリーIDの配列。")] int[]? categories,
        [Description("タグIDの配列。")] int[]? tags,
        [Description("アイキャッチ画像のID。")] int? featuredMedia,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WordPressService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();
        var store = ConnectionStore.Load();
        var activeConnectionName = store.ActiveConnection;
        if (string.IsNullOrWhiteSpace(activeConnectionName))
        {
            return "Error: No active connection.";
        }
        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, activeConnectionName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return $"Error: Active connection '{activeConnectionName}' not found.";
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
            using var writer = new StringWriter();
            OutputFormatter.WritePost(post, OutputFormat.Table, writer);
            return writer.ToString();
        }
        return "Error: Failed to create post.";
    }

    [McpServerTool]
    [Description("新しいカテゴリを作成します。")]
    public static async Task<string> CreateCategory(
        [Description("カテゴリ名。")] string name,
        [Description("スラッグ。")] string? slug,
        [Description("説明。")] string? description,
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
        
        using var writer = new StringWriter();
        OutputFormatter.WriteCategory(category, OutputFormat.Table, writer);
        return writer.ToString();
    }

    [McpServerTool]
    [Description("新しいタグを作成します。")]
    public static async Task<string> CreateTag(
        [Description("タグ名。")] string name,
        [Description("スラッグ。")] string? slug,
        [Description("説明。")] string? description,
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

        using var writer = new StringWriter();
        OutputFormatter.WriteTag(tag, OutputFormat.Table, writer);
        return writer.ToString();
    }

    [McpServerTool]
    [Description("メディアファイルをアップロードします。")]
    public static async Task<string> UploadMedia(
        [Description("アップロードするファイルへのパス。")] string filePath,
        [Description("メディアアイテムのタイトル。")] string? title,
        [Description("メディアアイテムの説明。")] string? description,
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
        
        using var writer = new StringWriter();
        OutputFormatter.WriteMediaItem(mediaItem, OutputFormat.Table, writer);
        return writer.ToString();
    }

    [McpServerTool]
    [Description("同期の競合を解決します。")]
    public static async Task<string> ResolveConflict(
        [Description("競合のタイプ ('post', 'category', 'tag')。")] string type,
        [Description("競合したアイテムのID。")] int id,
        [Description("解決戦略 ('local-wins' または 'server-wins')。")] string strategy,
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
        var activeConnectionName = store.ActiveConnection;
        if (string.IsNullOrWhiteSpace(activeConnectionName))
        {
            return "Error: No active connection.";
        }
        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, activeConnectionName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return $"Error: Active connection '{activeConnectionName}' not found.";
        }

        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await syncService.ResolveConflictAsync(type, id, strategy, profile, CancellationToken.None);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
        return writer.ToString();
    }

    // --- Delete/Organize Group ---

    [McpServerTool]
    [Description("投稿を削除します。")]
    public static async Task<string> DeletePost(
        [Description("削除する投稿のID。")] int id,
        [Description("完全に削除し、ゴミ箱をスキップします。")] bool force,
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
            using var writer = new StringWriter();
            OutputFormatter.WriteDeleteResponse(response, OutputFormat.Table, writer);
            return writer.ToString();
        }
        catch (WpAiCli.WordPress.WordPressApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cacheService.DeletePostFromCache(id);
            using var writer = new StringWriter();
            OutputFormatter.WriteDeleteResponse(new WordPressDeleteResponse { Deleted = true, Previous = ToJsonElementDict(new { title = new { raw = "Unknown (already deleted)" } }) }, OutputFormat.Table, writer);
            return writer.ToString();
        }
    }

    [McpServerTool]
    [Description("カテゴリを削除します。")]
    public static async Task<string> DeleteCategory(
        [Description("削除するカテゴリのID。")] int id,
        [Description("完全に削除します。")] bool force,
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
            using var writer = new StringWriter();
            OutputFormatter.WriteDeleteResponse(response, OutputFormat.Table, writer);
            return writer.ToString();
        }
        catch (WpAiCli.WordPress.WordPressApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cacheService.DeleteCategoryFromCache(id);
            using var writer = new StringWriter();
            OutputFormatter.WriteDeleteResponse(new WordPressDeleteResponse { Deleted = true, Previous = ToJsonElementDict(new { name = "Unknown (already deleted)" }) }, OutputFormat.Table, writer);
            return writer.ToString();
        }
    }

    [McpServerTool]
    [Description("タグを削除します。")]
    public static async Task<string> DeleteTag(
        [Description("削除するタグのID。")] int id,
        [Description("完全に削除します。")] bool force,
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
            using var writer = new StringWriter();
            OutputFormatter.WriteDeleteResponse(response, OutputFormat.Table, writer);
            return writer.ToString();
        }
        catch (WpAiCli.WordPress.WordPressApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cacheService.DeleteTagFromCache(id);
            using var writer = new StringWriter();
            OutputFormatter.WriteDeleteResponse(new WordPressDeleteResponse { Deleted = true, Previous = ToJsonElementDict(new { name = "Unknown (already deleted)" }) }, OutputFormat.Table, writer);
            return writer.ToString();
        }
    }

    [McpServerTool]
    [Description("メディアアイテムを削除します。")]
    public static async Task<string> DeleteMedia(
        [Description("削除するメディアアイテムのID。")] int id,
        [Description("完全に削除し、ゴミ箱をスキップします。")] bool force,
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
            using var writer = new StringWriter();
            OutputFormatter.WriteDeleteResponse(response, OutputFormat.Table, writer);
            return writer.ToString();
        }
        catch (WpAiCli.WordPress.WordPressApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            cacheService.DeleteMediaFromCache(id);
            using var writer = new StringWriter();
            OutputFormatter.WriteDeleteResponse(new WordPressDeleteResponse { Deleted = true, Previous = ToJsonElementDict(new { title = new { raw = "Unknown (already deleted)" } }) }, OutputFormat.Table, writer);
            return writer.ToString();
        }
    }

    [McpServerTool]
    [Description("ローカルのリビジョンキャッシュを削除します。")]
    public static Task<string> CleanRevisions(
        [Description("特定の投稿IDのキャッシュのみを削除する場合に指定。")] int? postId,
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
    [Description("ローカルの投稿ファイルをステータスに基づいてサブフォルダに整理します。")]
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