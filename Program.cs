using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WpAiCli.Configuration;
using WpAiCli.Completion;
using WpAiCli.Help;
using WpAiCli.Output;
using WpAiCli.Parsing;
using WpAiCli.Services;
using WpAiCli.WordPress.Models;
using Markdig;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        string? globalConnectionName;
        var remainingArgs = ExtractGlobalOptions(args, out globalConnectionName);

        if (remainingArgs.Length == 0)
        {
            PrintDocs();
            return (int)ExitCode.Success;
        }

        var command = remainingArgs[0].ToLowerInvariant();
        var commandArgs = remainingArgs.Skip(1).ToArray();

        // Handle commands that don't require a connection/DI first
        switch (command)
        {
            case "--help":
            case "-h":
            case "help":
            case "docs":
                PrintDocs();
                return (int)ExitCode.Success;
            case "--version":
            case "-V":
                var version = typeof(Program).Assembly.GetName().Version;
                Console.WriteLine(version?.ToString() ?? "unknown");
                return (int)ExitCode.Success;
            case "connections":
                return HandleConnections(commandArgs);
            case "completion":
                return HandleCompletion(commandArgs);
        }

        try
        {
            var (store, profile, token) = ResolveConnection(globalConnectionName);

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton(store);
                    services.AddSingleton(profile);
                    services.AddSingleton(new WordPressSettings(profile.BaseUrl, token));

                    services.AddTransient<WordPressService>();
                    services.AddTransient<CacheService>(sp => new CacheService(profile.CachePath!, profile.Name));
                    services.AddTransient<SyncService>();
                })
                .Build();

            UpdateLastUsedConnection(store, profile.Name);

            return await RunCommandAsync(host.Services, command, commandArgs);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.InvalidArguments;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return (int)ExitCode.UnhandledError;
        }
    }

    private static async Task<int> RunCommandAsync(IServiceProvider services, string command, string[] commandArgs)
    {
        try
        {
            switch (command)
            {
                case "posts":
                    return await HandlePostsAsync(
                        commandArgs,
                        services.GetRequiredService<WordPressService>(),
                        services.GetRequiredService<SyncService>(),
                        services.GetRequiredService<ConnectionProfile>(),
                        services.GetRequiredService<CacheService>()
                    );
                case "categories":
                    return await HandleCategoriesAsync(commandArgs, services.GetRequiredService<WordPressService>());
                case "tags":
                    return await HandleTagsAsync(commandArgs, services.GetRequiredService<WordPressService>());
                case "media":
                    return await HandleMediaAsync(
                        commandArgs,
                        services.GetRequiredService<WordPressService>(),
                        services.GetRequiredService<SyncService>(),
                        services.GetRequiredService<ConnectionProfile>()
                        );
                case "resolve":
                    return await HandleResolveAsync(commandArgs, services.GetRequiredService<SyncService>(), services.GetRequiredService<ConnectionProfile>());
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    return (int)ExitCode.InvalidArguments;
            }
        }
        catch (WpAiCli.WordPress.WordPressApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
            {
                Console.Error.WriteLine(ex.ResponseBody);
            }
            return (int)ExitCode.ApiError;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.InvalidArguments;
        }
    }


    static void PrintDocs()
    {
        if (!HelpPrinter.TryPrintDocumentation(Console.Out))
        {
            Console.Error.WriteLine("Help files not found. Place README.md or HOWTO.md alongside the executable.");
        }
    }

    static async Task<int> HandlePostsAsync(string[] args, WordPressService service, SyncService syncService, ConnectionProfile profile, CacheService cacheService)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Specify posts subcommand (list|get|create|update|delete|revisions|revision).");
            return (int)ExitCode.InvalidArguments;
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();
        var parsed = OptionParser.Parse(subArgs);
        var format = OutputFormatter.ParseFormat(parsed.GetString("format"));
        var ct = CancellationToken.None;

        switch (subcommand)
        {
            case "sync":
                return await HandlePostsSyncAsync(syncService, profile);

            case "list":
            {
                var status = parsed.GetString("status");
                var perPage = parsed.GetInt("per-page") ?? 10;
                perPage = Math.Clamp(perPage, 1, 100);
                var page = parsed.GetInt("page") ?? 1;
                page = Math.Max(page, 1);

                var posts = await service.ListPostsAsync(status, perPage, page, ct).ConfigureAwait(false);
                OutputFormatter.WritePosts(posts, format, Console.Out);

                return (int)ExitCode.Success;
            }

            case "get":
            {
                var id = ResolveId(parsed, defaultValue: null);
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a post ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var post = await service.GetPostAsync(id.Value, ct).ConfigureAwait(false);
                OutputFormatter.WritePost(post, format, Console.Out);

                return (int)ExitCode.Success;
            }

            case "create":
            {
                var title = parsed.GetString("title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    Console.Error.WriteLine("Provide --title.");
                    return (int)ExitCode.InvalidArguments;
                }

                var content = parsed.GetString("content");
                var contentFile = ToFileInfo(parsed.GetString("content-file"));
                var status = parsed.GetString("status") ?? "draft";
                var categories = parsed.GetIntArray("categories");
                var tags = parsed.GetIntArray("tags");
                var featured = parsed.GetInt("featured-media");
                var editMode = parsed.GetString("edit-mode") ?? "markdown";

                if (editMode != "markdown" && editMode != "html")
                {
                    Console.Error.WriteLine("Invalid value for --edit-mode. Must be 'markdown' or 'html'.");
                    return (int)ExitCode.InvalidArguments;
                }

                var rawContent = ContentLoader.ReadContent(content, contentFile) ?? string.Empty;

                var request = new WordPressCreatePostRequest
                {
                    Title = title,
                    Status = status,
                    Categories = categories,
                    Tags = tags,
                    FeaturedMedia = featured
                };

                var conversion = profile.MarkdownConversion ?? "client";

                if (editMode == "markdown")
                {
                    request.Meta = new Dictionary<string, object> { { "_md_source", rawContent } };
                    if (conversion == "client")
                    {
                        request.Content = Markdown.ToHtml(rawContent);
                    }
                    else // server conversion
                    {
                        request.Content = rawContent;
                    }
                }
                else // html mode
                {
                    request.Content = rawContent;
                }

                var post = await service.CreatePostAsync(request, ct).ConfigureAwait(false);
                OutputFormatter.WritePost(post, format, Console.Out);

                return (int)ExitCode.Success;
            }

            case "update":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a post ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var content = parsed.GetString("content");
                var contentFile = ToFileInfo(parsed.GetString("content-file"));
                var categories = parsed.GetIntArray("categories");
                var tags = parsed.GetIntArray("tags");
                var featured = parsed.GetInt("featured-media");
                var editMode = parsed.GetString("edit-mode") ?? "html"; // Default to html for updates

                if (editMode != "markdown" && editMode != "html")
                {
                    Console.Error.WriteLine("Invalid value for --edit-mode. Must be 'markdown' or 'html'.");
                    return (int)ExitCode.InvalidArguments;
                }

                var rawContent = ContentLoader.ReadContent(content, contentFile);

                var request = new WordPressUpdatePostRequest
                {
                    Title = parsed.GetString("title"),
                    Status = parsed.GetString("status"),
                    Categories = categories,
                    Tags = tags,
                    FeaturedMedia = featured
                };

                if (rawContent != null)
                {
                    var conversion = profile.MarkdownConversion ?? "client";
                    if (editMode == "markdown")
                    {
                        request.Meta = new Dictionary<string, object> { { "_md_source", rawContent } };
                        if (conversion == "client")
                        {
                            request.Content = Markdown.ToHtml(rawContent);
                        }
                        else // server conversion
                        {
                            request.Content = rawContent;
                        }
                    }
                    else // html mode
                    {
                        request.Content = rawContent;
                    }
                }

                var post = await service.UpdatePostAsync(id.Value, request, ct).ConfigureAwait(false);
                OutputFormatter.WritePost(post, format, Console.Out);

                return (int)ExitCode.Success;
            }

            case "delete":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a post ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var response = await service.DeletePostAsync(id.Value, force, ct).ConfigureAwait(false);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out);

                // Also remove local cache files for this post when server deletion succeeds
                if (response.Deleted)
                {
                    cacheService.DeletePostFromCache(id.Value);
                }

                return (int)ExitCode.Success;
            }

            case "revisions":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a post ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var revisions = await service.GetPostRevisionsAsync(id.Value, ct).ConfigureAwait(false);
                OutputFormatter.WriteRevisions(revisions, format, Console.Out);
                return (int)ExitCode.Success;
            }

            case "revision":
            {
                if (parsed.Positionals.Count < 2)
                {
                    Console.Error.WriteLine("Provide a post ID and a revision ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                if (!int.TryParse(parsed.Positionals[0], out var postId) || !int.TryParse(parsed.Positionals[1], out var revisionId))
                {
                    Console.Error.WriteLine("Post ID and revision ID must be integers.");
                    return (int)ExitCode.InvalidArguments;
                }

                var revision = await service.GetPostRevisionAsync(postId, revisionId, ct).ConfigureAwait(false);
                OutputFormatter.WriteRevision(revision, format, Console.Out);
                return (int)ExitCode.Success;
            }

            default:
                Console.Error.WriteLine($"Unknown posts subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }
    }

    static async Task<int> HandleResolveAsync(string[] args, SyncService syncService, ConnectionProfile profile)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: wpai resolve <type> <id> --strategy <local-wins|server-wins>");
            return (int)ExitCode.InvalidArguments;
        }

        var type = args[0].ToLowerInvariant();
        if (!int.TryParse(args[1], out var id))
        {
            Console.Error.WriteLine("ID must be an integer.");
            return (int)ExitCode.InvalidArguments;
        }

        var subArgs = args.Skip(2).ToArray();
        var parsed = OptionParser.Parse(subArgs);
        var strategy = parsed.GetString("strategy");

        if (strategy != "local-wins" && strategy != "server-wins")
        {
            Console.Error.WriteLine("Invalid strategy. Must be one of: local-wins, server-wins");
            return (int)ExitCode.InvalidArguments;
        }

        await syncService.ResolveConflictAsync(type, id, strategy, profile, CancellationToken.None);
        return (int)ExitCode.Success;
    }

    static async Task<int> HandlePostsSyncAsync(SyncService syncService, ConnectionProfile profile)
    {
        Console.WriteLine("Starting synchronization...");
        var syncLimit = profile.SyncItemsLimit ?? 30;
        var report = await syncService.SynchronizePostsAsync(profile, syncLimit, CancellationToken.None);
        PrintSyncReport(report);
        return (int)ExitCode.Success;
    }

    static async Task<int> HandleMediaSyncAsync(SyncService syncService, ConnectionProfile profile)
    {
        Console.WriteLine("Starting media synchronization...");
        var syncLimit = profile.SyncItemsLimit ?? 30;
        var report = await syncService.SynchronizeMediaAsync(syncLimit, CancellationToken.None);
        PrintSyncReport(report);
        return (int)ExitCode.Success;
    }

    static void PrintSyncReport(SyncReport report)
    {
        Console.WriteLine("\n--- Sync Report ---");
        Console.WriteLine($"Pushed to server: {report.PushedToServer.Count} post(s)");
        Console.WriteLine($"Pulled from server: {report.PulledFromServer.Count} post(s)");
        Console.WriteLine($"Newly cached: {report.NewlyCached.Count} post(s)");
        Console.WriteLine($"Deleted from local: {report.DeletedFromLocal.Count} post(s)");
        Console.WriteLine($"Conflicts detected (skipped): {report.ConflictDetected.Count} post(s)");
        if (report.PushedTaxonomies.Count > 0)
        {
            Console.WriteLine($"Pushed taxonomies: {report.PushedTaxonomies.Count}");
        }
        if (report.PushedMediaToServer.Count > 0 || report.NewlyCachedMedia.Count > 0 || report.MediaConflicts.Count > 0)
        {
            Console.WriteLine("--- Media ---");
            Console.WriteLine($"Pushed metadata to server: {report.PushedMediaToServer.Count} item(s)");
            Console.WriteLine($"Newly cached from server: {report.NewlyCachedMedia.Count} item(s)");
            Console.WriteLine($"Conflicts/Errors: {report.MediaConflicts.Count} item(s)");
        }
        if (report.ConflictDetected.Count > 0)
        {
            Console.WriteLine("\nConflicts detected for the following Post IDs:");
            Console.WriteLine($"  {string.Join(", ", report.ConflictDetected)}");
            Console.WriteLine("Please resolve them individually using the 'resolve' command.");
            Console.WriteLine("Example: wpai resolve post 123 --strategy [local-wins|server-wins]");
        }
        Console.WriteLine("-------------------");
    }

    static async Task<int> HandleCategoriesAsync(string[] args, WordPressService service)
    {
        if (args.Length == 0)
        {
            args = new[] { "list" };
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();
        var parsed = OptionParser.Parse(subArgs);
        var format = OutputFormatter.ParseFormat(parsed.GetString("format"));
        var ct = CancellationToken.None;

        switch (subcommand)
        {
            case "list":
            {
                var categories = await service.ListCategoriesAsync(ct).ConfigureAwait(false);
                OutputFormatter.WriteCategories(categories, format, Console.Out);
                return (int)ExitCode.Success;
            }
            case "get":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a category ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var category = await service.GetCategoryAsync(id.Value, ct).ConfigureAwait(false);
                OutputFormatter.WriteCategory(category, format, Console.Out);

                return (int)ExitCode.Success;
            }
            case "create":
            {
                var name = parsed.GetString("name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.Error.WriteLine("Provide --name.");
                    return (int)ExitCode.InvalidArguments;
                }

                var request = new WordPressCreateCategoryRequest
                {
                    Name = name,
                    Slug = parsed.GetString("slug"),
                    Description = parsed.GetString("description")
                };

                var category = await service.CreateCategoryAsync(request, ct).ConfigureAwait(false);
                OutputFormatter.WriteCategory(category, format, Console.Out);
                return (int)ExitCode.Success;
            }
            case "update":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a category ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var request = new WordPressUpdateCategoryRequest
                {
                    Name = parsed.GetString("name"),
                    Slug = parsed.GetString("slug"),
                    Description = parsed.GetString("description")
                };

                var category = await service.UpdateCategoryAsync(id.Value, request, ct).ConfigureAwait(false);
                OutputFormatter.WriteCategory(category, format, Console.Out);
                return (int)ExitCode.Success;
            }
            case "delete":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a category ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var response = await service.DeleteCategoryAsync(id.Value, force, ct).ConfigureAwait(false);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out);

                return (int)ExitCode.Success;
            }
            default:
                Console.Error.WriteLine($"Unknown categories subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }
    }

    static async Task<int> HandleTagsAsync(string[] args, WordPressService service)
    {
        if (args.Length == 0)
        {
            args = new[] { "list" };
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();
        var parsed = OptionParser.Parse(subArgs);
        var format = OutputFormatter.ParseFormat(parsed.GetString("format"));
        var ct = CancellationToken.None;

        switch (subcommand)
        {
            case "list":
            {
                var tags = await service.ListTagsAsync(ct).ConfigureAwait(false);
                OutputFormatter.WriteTags(tags, format, Console.Out);
                return (int)ExitCode.Success;
            }
            case "create":
            {
                var name = parsed.GetString("name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.Error.WriteLine("Provide --name.");
                    return (int)ExitCode.InvalidArguments;
                }

                var request = new WordPressCreateTagRequest
                {
                    Name = name,
                    Slug = parsed.GetString("slug"),
                    Description = parsed.GetString("description")
                };

                var tag = await service.CreateTagAsync(request, ct).ConfigureAwait(false);
                OutputFormatter.WriteTag(tag, format, Console.Out);

                return (int)ExitCode.Success;
            }
            case "get":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a tag ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var tag = await service.GetTagAsync(id.Value, ct).ConfigureAwait(false);
                OutputFormatter.WriteTag(tag, format, Console.Out);

                return (int)ExitCode.Success;
            }
            case "delete":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a tag ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var response = await service.DeleteTagAsync(id.Value, force, ct).ConfigureAwait(false);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out);

                return (int)ExitCode.Success;
            }
            default:
                Console.Error.WriteLine($"Unknown tags subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }
    }

    static async Task<int> HandleMediaAsync(string[] args, WordPressService service, SyncService syncService, ConnectionProfile profile)
    {
        if (args.Length == 0)
        {
            args = new[] { "list" };
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();
        var parsed = OptionParser.Parse(subArgs);
        var format = OutputFormatter.ParseFormat(parsed.GetString("format"));
        var ct = CancellationToken.None;

        switch (subcommand)
        {
            case "sync":
                return await HandleMediaSyncAsync(syncService, profile);
            case "list":
            {
                var perPage = parsed.GetInt("per-page") ?? 10;
                perPage = Math.Clamp(perPage, 1, 100);
                var page = parsed.GetInt("page") ?? 1;
                page = Math.Max(page, 1);

                var mediaItems = await service.ListMediaAsync(perPage, page, ct).ConfigureAwait(false);
                OutputFormatter.WriteMediaItems(mediaItems, format, Console.Out);
                return (int)ExitCode.Success;
            }
            case "upload":
            {
                var filePath = parsed.Positionals.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    Console.Error.WriteLine("Provide a file path to upload.");
                    return (int)ExitCode.InvalidArguments;
                }

                var title = parsed.GetString("title");
                var description = parsed.GetString("description");

                var mediaItem = await service.UploadMediaAsync(filePath, title, description, ct).ConfigureAwait(false);
                OutputFormatter.WriteMediaItem(mediaItem, format, Console.Out);

                return (int)ExitCode.Success;
            }
            case "delete":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a media ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var response = await service.DeleteMediaAsync(id.Value, force, ct).ConfigureAwait(false);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out);

                return (int)ExitCode.Success;
            }
            default:
                Console.Error.WriteLine($"Unknown media subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }
    }

    static int HandleConnections(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Specify connections subcommand (list|add|update|remove).");
            return (int)ExitCode.InvalidArguments;
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();
        var parsed = OptionParser.Parse(subArgs);

        return subcommand switch
        {
            "list" => HandleConnectionsList(),
            "add" => HandleConnectionsAdd(parsed),
            "update" => HandleConnectionsUpdate(parsed),
            "remove" => HandleConnectionsRemove(),
            _ => UnknownConnectionsCommand(subcommand)
        };
    }

    static int UnknownConnectionsCommand(string subcommand)
    {
        Console.Error.WriteLine($"Unknown connections subcommand: {subcommand}");
        return (int)ExitCode.InvalidArguments;
    }

    static int HandleConnectionsList()
    {
        var store = ConnectionStore.Load();
        if (store.Profiles.Count == 0)
        {
            Console.WriteLine("No connections have been registered yet.");
            return (int)ExitCode.Success;
        }

        Console.WriteLine("Registered connections:");
        for (int i = 0; i < store.Profiles.Count; i++)
        {
            var profile = store.Profiles[i];
            var isLastUsed = string.Equals(profile.Name, store.LastUsedConnection, StringComparison.OrdinalIgnoreCase);
            var marker = isLastUsed ? "*" : " ";
            Console.WriteLine($" {marker} {i + 1}. {profile.Name} ({profile.BaseUrl})");
        }

        if (!string.IsNullOrWhiteSpace(store.LastUsedConnection))
        {
            Console.WriteLine($"* indicates the last used connection ({store.LastUsedConnection}).");
        }

        return (int)ExitCode.Success;
    }

    static int HandleConnectionsAdd(ParsedOptions parsed)
    {
        var name = parsed.GetString("name");
        var baseUrl = parsed.GetString("base-url");
        var token = parsed.GetString("token");
        var cachePath = parsed.GetString("cache-path");
        var syncLimitStr = parsed.GetString("sync-limit");

        var markdownConversion = parsed.GetString("markdown-conversion");

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("Provide --name, --base-url, and --token when adding a connection.");
            return (int)ExitCode.InvalidArguments;
        }

        if (markdownConversion != null && markdownConversion != "client" && markdownConversion != "server")
        {
            Console.Error.WriteLine("Invalid value for --markdown-conversion. Must be 'client' or 'server'.");
            return (int)ExitCode.InvalidArguments;
        }

        var store = ConnectionStore.Load();
        if (store.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"Connection '{name}' already exists. Remove it first if you need to redefine it.");
            return (int)ExitCode.InvalidArguments;
        }

        var absoluteCachePath = !string.IsNullOrWhiteSpace(cachePath) ? Path.GetFullPath(cachePath) : null;
        int? syncLimit = int.TryParse(syncLimitStr, out var parsedLimit) ? parsedLimit : null;

        var profile = new ConnectionProfile
        {
            Name = name,
            BaseUrl = baseUrl.Trim(),
            CredentialKey = $"WpAiCli/{name}",
            CachePath = absoluteCachePath,
            SyncItemsLimit = syncLimit,
            MarkdownConversion = markdownConversion
        };

        CredentialManager.Save(profile.CredentialKey, token);
        store.Profiles.Add(profile);
        store.LastUsedConnection = profile.Name;
        store.Save();

        Console.WriteLine($"Connection '{profile.Name}' registered.");
        if (profile.CachePath is not null)
        {
            Console.WriteLine($"Cache location: {profile.CachePath}");
        }
        if (profile.SyncItemsLimit is not null)
        {
            Console.WriteLine($"Sync limit: {profile.SyncItemsLimit}");
        }
        return (int)ExitCode.Success;
    }

    static int HandleConnectionsUpdate(ParsedOptions parsed)
    {
        var name = parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Specify the name of the connection to update.");
            return (int)ExitCode.InvalidArguments;
        }

        var cachePath = parsed.GetString("cache-path");
        var syncLimitStr = parsed.GetString("sync-limit");
        var markdownConversion = parsed.GetString("markdown-conversion");

        if (cachePath is null && syncLimitStr is null && markdownConversion is null)
        {
            Console.Error.WriteLine("Specify the setting to update. Supported options: --cache-path, --sync-limit, --markdown-conversion");
            return (int)ExitCode.InvalidArguments;
        }

        var store = ConnectionStore.Load();
        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            Console.Error.WriteLine($"Connection '{name}' not found.");
            return (int)ExitCode.InvalidArguments;
        }

        var updated = false;

        if (cachePath is not null)
        {
            profile.CachePath = !string.IsNullOrWhiteSpace(cachePath) ? Path.GetFullPath(cachePath) : null;
            updated = true;
            Console.WriteLine(profile.CachePath is not null
                ? $"Cache location set to: {profile.CachePath}"
                : "Cache location has been removed.");
        }

        if (syncLimitStr is not null)
        {
            if (int.TryParse(syncLimitStr, out var parsedLimit) && parsedLimit > 0)
            {
                profile.SyncItemsLimit = parsedLimit;
                Console.WriteLine($"Sync limit set to: {profile.SyncItemsLimit}");
            }
            else if (string.IsNullOrEmpty(syncLimitStr))
            {
                profile.SyncItemsLimit = null;
                Console.WriteLine("Sync limit has been reset to default (30).");
            }
            else
            {
                Console.Error.WriteLine($"Invalid value for --sync-limit: '{syncLimitStr}'. Must be a positive integer.");
                return (int)ExitCode.InvalidArguments;
            }
            updated = true;
        }

        if (markdownConversion is not null)
        {
            if (markdownConversion == "client" || markdownConversion == "server")
            {
                profile.MarkdownConversion = markdownConversion;
                Console.WriteLine($"Markdown conversion strategy set to: {profile.MarkdownConversion}");
            }
            else if (string.IsNullOrEmpty(markdownConversion))
            {
                profile.MarkdownConversion = null; // Reset to default
                Console.WriteLine("Markdown conversion strategy has been reset to default (client).");
            }
            else
            {
                Console.Error.WriteLine($"Invalid value for --markdown-conversion: '{markdownConversion}'. Must be 'client' or 'server'.");
                return (int)ExitCode.InvalidArguments;
            }
            updated = true;
        }

        if (updated)
        {
            store.Save();
            Console.WriteLine($"\nConnection '{name}' updated.");
        }

        return (int)ExitCode.Success;
    }

    static int HandleConnectionsRemove()
    {
        var store = ConnectionStore.Load();
        if (store.Profiles.Count == 0)
        {
            Console.WriteLine("No connections available to remove.");
            return (int)ExitCode.Success;
        }

        Console.WriteLine("Select the connection to remove:");
        for (int i = 0; i < store.Profiles.Count; i++)
        {
            Console.WriteLine($" {i + 1}. {store.Profiles[i].Name}");
        }

        Console.Write("Enter number (blank to cancel): ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine("Removal cancelled.");
            return (int)ExitCode.Success;
        }

        if (!int.TryParse(input, out var choice) || choice < 1 || choice > store.Profiles.Count)
        {
            Console.Error.WriteLine("Invalid selection.");
            return (int)ExitCode.InvalidArguments;
        }

        var profile = store.Profiles[choice - 1];
        Console.Write($"Delete connection '{profile.Name}'? (y/N): ");
        var confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Removal cancelled.");
            return (int)ExitCode.Success;
        }

        CredentialManager.Delete(profile.CredentialKey);
        store.Profiles.RemoveAt(choice - 1);
        if (string.Equals(store.LastUsedConnection, profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            store.LastUsedConnection = store.Profiles.FirstOrDefault()?.Name;
        }

        store.Save();
        Console.WriteLine("Connection removed.");
        return (int)ExitCode.Success;
    }

    static int HandleCompletion(string[] args)
    {
        var parsed = OptionParser.Parse(args);
        var shell = parsed.GetString("shell") ?? parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(shell))
        {
            Console.Error.WriteLine("Specify a shell via --shell (bash|zsh|powershell).");
            return (int)ExitCode.InvalidArguments;
        }

        try
        {
            Console.WriteLine(CompletionScriptGenerator.Generate(shell));
            return (int)ExitCode.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.InvalidArguments;
        }
    }

    static string[] ExtractGlobalOptions(string[] source, out string? connectionOverride)
    {
        connectionOverride = null;
        var remaining = new List<string>();

        for (int i = 0; i < source.Length; i++)
        {
            var token = source[i];
            if (!token.StartsWith("--"))
            {
                remaining.Add(token);
                continue;
            }

            if (TryMatchOption(token, "connection", out var inlineValue))
            {
                connectionOverride = ExtractValue(source, ref i, inlineValue);
                continue;
            }

            remaining.Add(token);
        }

        connectionOverride = string.IsNullOrWhiteSpace(connectionOverride) ? null : connectionOverride.Trim();

        return remaining.ToArray();

        static bool TryMatchOption(string token, string optionName, out string? inlineValue)
        {
            var prefix = $"--{optionName}";
            if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                inlineValue = null;
                return false;
            }

            if (token.Length == prefix.Length)
            {
                inlineValue = null;
                return true;
            }

            if (token[prefix.Length] == '=')
            {
                inlineValue = token[(prefix.Length + 1)..];
                return true;
            }

            inlineValue = null;
            return false;
        }

        static string? ExtractValue(string[] source, ref int index, string? inlineValue)
        {
            if (inlineValue is not null)
            {
                return inlineValue;
            }

            if (index + 1 < source.Length && !source[index + 1].StartsWith("--"))
            {
                index++;
                return source[index];
            }

            return null;
        }
    }

    static (ConnectionStore Store, ConnectionProfile Profile, string Token) ResolveConnection(string? requestedName)
    {
        var store = ConnectionStore.Load();
        if (store.Profiles.Count == 0)
        {
            throw new InvalidOperationException("No connections registered. Use `wpai connections add` first.");
        }

        ConnectionProfile? profile = null;

        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, requestedName, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                throw new InvalidOperationException($"Connection '{requestedName}' was not found. Run `wpai connections list` to review names.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(store.LastUsedConnection))
        {
            profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, store.LastUsedConnection, StringComparison.OrdinalIgnoreCase));
        }

        profile ??= store.Profiles.Count == 1
            ? store.Profiles[0]
            : throw new InvalidOperationException("Multiple connections registered. Specify one with `--connection <name>`.");

        var token = CredentialManager.ReadSecret(profile.CredentialKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"Credential for connection '{profile.Name}' is missing. Re-add the connection.");
        }

        return (store, profile, token);
    }

    static void UpdateLastUsedConnection(ConnectionStore store, string connectionName)
    {
        if (!string.Equals(store.LastUsedConnection, connectionName, StringComparison.OrdinalIgnoreCase))
        {
            store.LastUsedConnection = connectionName;
            store.Save();
        }
    }

    static int? ResolveId(ParsedOptions parsed, string? defaultValue)
    {
        if (parsed.Positionals.Count > 0 && int.TryParse(parsed.Positionals[0], out var positionalId))
        {
            return positionalId;
        }

        if (!string.IsNullOrWhiteSpace(defaultValue) && int.TryParse(defaultValue, out var fallback))
        {
            return fallback;
        }

        return parsed.GetInt("id");
    }

    static FileInfo? ToFileInfo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new FileInfo(path);
    }
}
