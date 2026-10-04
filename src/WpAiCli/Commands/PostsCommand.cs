using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WpAiCli.Configuration;
using WpAiCli.Output;
using WpAiCli.Parsing;
using WpAiCli.Services;

namespace WpAiCli.Commands;

public static class PostsCommand
{
    public static async Task<int> ExecuteAsync(
        string[] args,
        WordPressService service,
        WorkspaceService workspaceService,
        ConnectionProfile profile,
        CacheService cacheService)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "help")
        {
            PrintHelp();
            return args.Length == 0 ? (int)ExitCode.InvalidArguments : (int)ExitCode.Success;
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();
        var parsed = OptionParser.Parse(subArgs);
        var format = OutputFormatter.ParseFormat(parsed.GetString("format"));
        var ct = CancellationToken.None;

        switch (subcommand)
        {
            case "organize":
            {
                Console.WriteLine("Organizing local post files by status...");
                var moved = cacheService.OrganizePostFiles();
                if (moved.Count > 0)
                {
                    Console.WriteLine($"Local post files organized successfully ({moved.Count} file(s) moved):");
                    foreach (var msg in moved)
                    {
                        Console.WriteLine($"  - {msg}");
                    }
                }
                else
                {
                    Console.WriteLine("Local post files are already in their correct status folders. No files were moved.");
                }
                return (int)ExitCode.Success;
            }
            case "pull":
            {
                Console.WriteLine("Pulling latest posts from server...");
                var syncLimit = profile.SyncItemsLimit ?? 30;
                var report = await workspaceService.PullPostsAsync(profile, syncLimit, ct);
                OutputFormatter.WriteTransferReport(report, Console.Out);
                return (int)ExitCode.Success;
            }
            case "sync":
            {
                Console.WriteLine("Starting two-way posts synchronization...");
                var syncLimit = profile.SyncItemsLimit ?? 30;
                var report = await workspaceService.SyncPostsAsync(profile, syncLimit, ct);
                OutputFormatter.WriteTransferReport(report, Console.Out);
                return (int)ExitCode.Success;
            }
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
                var id = CommandHelpers.ResolveId(parsed, defaultValue: null);
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
                var bodyContent = parsed.GetString("content");
                var contentFile = parsed.GetString("content-file");
                var status = parsed.GetString("status") ?? "draft";
                var categories = parsed.GetIntArray("categories");
                var tags = parsed.GetIntArray("tags");
                var featured = parsed.GetInt("featured-media");
                var editMode = parsed.GetString("edit-mode") ?? "markdown";

                try
                {
                    var (post, cacheResult) = await workspaceService.CreatePostAsync(
                        title!,
                        bodyContent,
                        contentFile,
                        status,
                        editMode,
                        categories,
                        tags,
                        featured,
                        profile,
                        ct);

                    if (cacheResult != null)
                    {
                        Console.WriteLine($"[Cache] Post created and saved to '{cacheResult.CurrentStatus}' folder ({Path.GetFileName(cacheResult.FilePath)}).");
                        Console.WriteLine($"[Next Step] Edit this file directly in the cache, then run 'wpai posts push {post.Id}' to sync changes.");
                    }

                    OutputFormatter.WritePost(post, format, Console.Out);
                    return (int)ExitCode.Success;
                }
                catch (ArgumentException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return (int)ExitCode.InvalidArguments;
                }
            }
            case "push":
            {
                if (parsed.GetBool("all", defaultValue: false))
                {
                    Console.WriteLine("Pushing all modified local posts to the server...");
                    var report = await workspaceService.PushAllModifiedPostsAsync(profile, ct);
                    OutputFormatter.WriteTransferReport(report, Console.Out);
                    return (int)ExitCode.Success;
                }
                else
                {
                    var id = CommandHelpers.ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                    if (id is null)
                    {
                        Console.Error.WriteLine("Provide a post ID or use the --all flag.");
                        return (int)ExitCode.InvalidArguments;
                    }

                    var (updated, cacheResult) = await workspaceService.PushPostAsync(id.Value, profile, ct);
                    if (cacheResult.WasMoved)
                    {
                        Console.WriteLine(cacheResult.MoveMessage);
                    }
                    OutputFormatter.WritePost(updated, format, Console.Out);
                    return (int)ExitCode.Success;
                }
            }
            case "delete":
            {
                var id = CommandHelpers.ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a post ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var (response, cachedTitle, alreadyDeleted) = await workspaceService.DeletePostAsync(id.Value, force, ct);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out, id.Value, cachedTitle);

                if (format == OutputFormat.Table)
                {
                    if (alreadyDeleted)
                    {
                        Console.WriteLine($"Post {id.Value} was already deleted on server. Local cache cleared.");
                    }
                    else if (response.Deleted)
                    {
                        Console.WriteLine($"Successfully deleted post {id.Value}.");
                    }
                }

                return (int)ExitCode.Success;
            }
            default:
                Console.Error.WriteLine($"Unknown posts subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: wpai posts <subcommand> [options]");
        Console.WriteLine("\nSubcommands:");
        Console.WriteLine("  list           List posts from the server");
        Console.WriteLine("  get <id>       Retrieve a single post by ID");
        Console.WriteLine("  pull           Pull latest posts and refresh local cache (Recommended)");
        Console.WriteLine("  push <id>      Push local post edits to the server (--all for all modified)");
        Console.WriteLine("  create         Create a new post");
        Console.WriteLine("  delete <id>    Delete a post");
        Console.WriteLine("  sync           Two-way synchronization for posts and taxonomies");
        Console.WriteLine("  organize       Organize local post files into status folders");
        Console.WriteLine("\nImportant for AI:");
        Console.WriteLine("  - Do NOT manually move or rename files in the cache. The tool relocates them automatically.");
        Console.WriteLine("  - When creating a new post:");
        Console.WriteLine("    * Preferred: If MCP tools are available, use 'CreatePost' to supply title and content directly.");
        Console.WriteLine("    * Via CLI: Run 'wpai posts create --title \"...\" --status draft' to create the post frame,");
        Console.WriteLine("      edit the generated file in 'wp-cache/.../posts/draft/', then run 'wpai posts push <id>'.");
        Console.WriteLine("    * Do NOT create temporary/throwaway markdown files outside the cache directory.");
    }
}
