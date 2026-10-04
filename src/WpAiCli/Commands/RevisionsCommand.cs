using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WpAiCli.Configuration;
using WpAiCli.Output;
using WpAiCli.Parsing;
using WpAiCli.Services;

namespace WpAiCli.Commands;

public static class RevisionsCommand
{
    public static async Task<int> ExecuteAsync(
        string[] args,
        WordPressService service,
        CacheService cacheService)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Specify revisions subcommand (list|fetch|clean).");
            return (int)ExitCode.InvalidArguments;
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
                var postId = parsed.GetInt("post-id");
                if (postId is null)
                {
                    Console.Error.WriteLine("Provide a post ID using --post-id <ID>.");
                    return (int)ExitCode.InvalidArguments;
                }

                var revisions = await service.GetPostRevisionsAsync(postId.Value, ct).ConfigureAwait(false);
                OutputFormatter.WriteRevisions(revisions, format, Console.Out);
                return (int)ExitCode.Success;
            }
            case "fetch":
            {
                var postId = parsed.GetInt("post-id");
                if (postId is null)
                {
                    Console.Error.WriteLine("Provide a post ID using --post-id <ID>.");
                    return (int)ExitCode.InvalidArguments;
                }

                var revisions = await service.GetPostRevisionsAsync(postId.Value, ct).ConfigureAwait(false);
                if (revisions == null || !revisions.Any())
                {
                    Console.WriteLine($"No revisions found for post {postId.Value}.");
                    return (int)ExitCode.Success;
                }

                Console.WriteLine($"Fetching {revisions.Count()} revisions for post {postId.Value}...");
                foreach (var revisionSummary in revisions)
                {
                    Console.WriteLine($"  -> Fetching revision {revisionSummary.Id}...");
                    var fullRevision = await service.GetPostRevisionAsync(postId.Value, revisionSummary.Id, ct).ConfigureAwait(false);
                    cacheService.SaveRevisionToCache(fullRevision);
                }

                Console.WriteLine($"\nFetch complete. Revisions are saved in: wp-cache/revisions/post_{postId.Value}/");
                return (int)ExitCode.Success;
            }
            case "clean":
            {
                var postId = parsed.GetInt("post-id");
                
                string targetDescription = postId.HasValue
                    ? $"the revision cache for post {postId.Value}"
                    : "ALL local revision caches";

                Console.Write($"Are you sure you want to permanently delete {targetDescription}? (y/N): ");
                var confirmation = Console.ReadLine();
                if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Operation cancelled.");
                    return (int)ExitCode.Success;
                }

                cacheService.CleanRevisionsCache(postId);
                return (int)ExitCode.Success;
            }
            default:
                Console.Error.WriteLine($"Unknown revisions subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }
    }
}
