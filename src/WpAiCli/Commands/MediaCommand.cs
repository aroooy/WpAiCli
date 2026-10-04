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

public static class MediaCommand
{
    public static async Task<int> ExecuteAsync(
        string[] args,
        WordPressService service,
        WorkspaceService workspaceService,
        ConnectionProfile profile,
        CacheService cacheService)
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
            case "pull":
            case "sync":
                return await HandleMediaSyncAsync(subcommand, workspaceService, profile);
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
                if (parsed.GetBool("help", defaultValue: false))
                {
                    Console.WriteLine("Usage: wpai media upload [options] [--file] <file_path>");
                    Console.WriteLine("Uploads a media file.");
                    Console.WriteLine("\nArguments:");
                    Console.WriteLine("  <file_path>        The path to the file to upload (can also be provided with --file).");
                    Console.WriteLine("\nOptions:");
                    Console.WriteLine("  --file <path>      The path to the file to upload.");
                    Console.WriteLine("  --title <title>    The title for the media item.");
                    Console.WriteLine("  --description <desc> The description for the media item.");
                    Console.WriteLine("  --format <format>  The output format (json|table|yaml).");
                    Console.WriteLine("  --help             Show help for the upload command.");
                    return (int)ExitCode.Success;
                }

                var filePath = parsed.GetString("file") ?? parsed.Positionals.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    Console.Error.WriteLine("Provide a file path to upload using --file <path> or as a positional argument.");
                    Console.Error.WriteLine("Use 'wpai media upload --help' for more information.");
                    return (int)ExitCode.InvalidArguments;
                }

                var title = parsed.GetString("title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = Path.GetFileNameWithoutExtension(filePath);
                }

                var description = parsed.GetString("description");

                var mediaItem = await service.UploadMediaAsync(filePath, title, description, ct).ConfigureAwait(false);
                try
                {
                    if (!string.IsNullOrEmpty(mediaItem.SourceUrl))
                    {
                        var bytes = await service.DownloadMediaFileAsync(mediaItem.SourceUrl!, ct).ConfigureAwait(false);
                        cacheService.SaveMediaToCache(mediaItem, bytes);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: failed to cache uploaded media: {ex.Message}");
                }
                OutputFormatter.WriteMediaItem(mediaItem, format, Console.Out);

                return (int)ExitCode.Success;
            }
            case "push":
            {
                var id = CommandHelpers.ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a media ID.");
                    return (int)ExitCode.InvalidArguments;
                }
                var updated = await workspaceService.PushMediaAsync(id.Value, ct);
                OutputFormatter.WriteMediaItem(updated, format, Console.Out);
                return (int)ExitCode.Success;
            }
            case "delete":
            {
                var id = CommandHelpers.ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a media ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var (response, cachedTitle, alreadyDeleted) = await workspaceService.DeleteMediaAsync(id.Value, force, ct);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out, id.Value, cachedTitle);

                if (format == OutputFormat.Table)
                {
                    if (alreadyDeleted)
                    {
                        Console.WriteLine($"Media {id.Value} was already deleted on server. Local cache cleared.");
                    }
                    else if (response.Deleted)
                    {
                        Console.WriteLine($"Successfully deleted media {id.Value}.");
                    }
                }

                return (int)ExitCode.Success;
            }
            default:
                Console.Error.WriteLine($"Unknown media subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }
    }

    private static async Task<int> HandleMediaSyncAsync(string subcommand, WorkspaceService workspaceService, ConnectionProfile profile)
    {
        var isPull = string.Equals(subcommand, "pull", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine(isPull ? "Pulling media from server..." : "Starting two-way media synchronization...");
        var syncLimit = profile.SyncItemsLimit ?? 30;
        var report = isPull
            ? await workspaceService.PullMediaAsync(syncLimit, CancellationToken.None)
            : await workspaceService.SyncMediaAsync(syncLimit, CancellationToken.None);
        OutputFormatter.WriteTransferReport(report, Console.Out);
        return (int)ExitCode.Success;
    }
}
