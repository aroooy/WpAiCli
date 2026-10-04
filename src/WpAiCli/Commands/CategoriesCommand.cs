using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WpAiCli.Configuration;
using WpAiCli.Output;
using WpAiCli.Parsing;
using WpAiCli.Services;

namespace WpAiCli.Commands;

public static class CategoriesCommand
{
    public static async Task<int> ExecuteAsync(
        string[] args,
        WordPressService service,
        CacheService cacheService,
        WorkspaceService workspaceService)
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
                var id = CommandHelpers.ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
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

                var category = await workspaceService.CreateCategoryAsync(
                    name,
                    parsed.GetString("slug"),
                    parsed.GetString("description"),
                    ct).ConfigureAwait(false);

                OutputFormatter.WriteCategory(category, format, Console.Out);
                return (int)ExitCode.Success;
            }
            case "push":
            {
                var id = CommandHelpers.ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a category ID.");
                    return (int)ExitCode.InvalidArguments;
                }
                var updated = await workspaceService.PushCategoryAsync(id.Value, ct);
                OutputFormatter.WriteCategory(updated, format, Console.Out);
                return (int)ExitCode.Success;
            }
            case "delete":
            {
                var id = CommandHelpers.ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a category ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var (response, cachedName, alreadyDeleted) = await workspaceService.DeleteCategoryAsync(id.Value, force, ct);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out, id.Value, cachedName);

                if (format == OutputFormat.Table)
                {
                    if (alreadyDeleted)
                    {
                        Console.WriteLine($"Category {id.Value} was already deleted on server. Local cache cleared.");
                    }
                    else if (response.Deleted)
                    {
                        Console.WriteLine($"Successfully deleted category {id.Value}.");
                    }
                }

                return (int)ExitCode.Success;
            }
            default:
                Console.Error.WriteLine($"Unknown categories subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }
    }
}
