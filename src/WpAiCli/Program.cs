using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WpAiCli.Commands;
using WpAiCli.Configuration;
using WpAiCli.Help;
using WpAiCli.Services;
using WpAiCli.WordPress;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintDocs();
            return (int)ExitCode.Success;
        }

        var command = args[0].ToLowerInvariant();
        var commandArgs = args.Skip(1).ToArray();

        // Handle commands that don't require an active connection or DI first
        switch (command)
        {
            case "cache":
                return CacheCommand.Execute(commandArgs);
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
                return ConnectionsCommand.Execute(commandArgs);
            case "completion":
                return CompletionCommand.Execute(commandArgs);
            case "export-plugin":
                return PluginExportCommand.Execute();
            case "mcp":
                return await McpCommand.ExecuteAsync(commandArgs);
        }

        try
        {
            var (store, profile, credential) = CommandHelpers.ResolveConnection();

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                })
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton(store);
                    services.AddSingleton(profile);
                    services.AddHttpClient<WordPressApiClient>();
                    services.AddSingleton(sp => 
                        new WordPressApiClient(
                            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(WordPressApiClient)),
                            profile,
                            credential
                        ));

                    services.AddTransient<WordPressService>();
                    services.AddTransient<CacheService>(sp => new CacheService(profile.CachePath!, profile.Name));
                    services.AddTransient<WorkspaceService>();
                })
                .Build();

            CommandHelpers.UpdateLastUsedConnection(store, profile.Name);

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
                    return await PostsCommand.ExecuteAsync(
                        commandArgs,
                        services.GetRequiredService<WordPressService>(),
                        services.GetRequiredService<WorkspaceService>(),
                        services.GetRequiredService<ConnectionProfile>(),
                        services.GetRequiredService<CacheService>()
                    );
                case "categories":
                    return await CategoriesCommand.ExecuteAsync(
                        commandArgs,
                        services.GetRequiredService<WordPressService>(),
                        services.GetRequiredService<CacheService>(),
                        services.GetRequiredService<WorkspaceService>()
                    );
                case "tags":
                    return await TagsCommand.ExecuteAsync(
                        commandArgs,
                        services.GetRequiredService<WordPressService>(),
                        services.GetRequiredService<CacheService>(),
                        services.GetRequiredService<WorkspaceService>()
                    );
                case "media":
                    return await MediaCommand.ExecuteAsync(
                        commandArgs,
                        services.GetRequiredService<WordPressService>(),
                        services.GetRequiredService<WorkspaceService>(),
                        services.GetRequiredService<ConnectionProfile>(),
                        services.GetRequiredService<CacheService>()
                    );
                case "taxonomies":
                    return await TaxonomiesCommand.ExecuteAsync(
                        commandArgs,
                        services.GetRequiredService<WorkspaceService>()
                    );
                case "revisions":
                    return await RevisionsCommand.ExecuteAsync(
                        commandArgs,
                        services.GetRequiredService<WordPressService>(),
                        services.GetRequiredService<CacheService>()
                    );
                case "resolve":
                    return await ResolveCommand.ExecuteAsync(
                        commandArgs,
                        services.GetRequiredService<WorkspaceService>(),
                        services.GetRequiredService<ConnectionProfile>()
                    );
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    return (int)ExitCode.InvalidArguments;
            }
        }
        catch (WordPressApiException ex)
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

    private static void PrintDocs()
    {
        if (!HelpPrinter.TryPrintDocumentation(Console.Out))
        {
            Console.Error.WriteLine("Help files not found. Place README.md or HOWTO.md alongside the executable.");
        }
    }
}
