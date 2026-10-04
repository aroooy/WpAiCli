using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WpAiCli.Configuration;
using WpAiCli.Services;
using WpAiCli.WordPress;

namespace WpAiCli.Commands;

public static class McpCommand
{
    public static async Task<int> ExecuteAsync(string[] commandArgs)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(commandArgs);
            builder.Logging.ClearProviders();

            var (store, profile, credential) = CommandHelpers.ResolveConnection();

            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton(profile);
            builder.Services.AddHttpClient<WordPressApiClient>();
            builder.Services.AddSingleton(sp =>
                new WordPressApiClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(WordPressApiClient)),
                    profile,
                    credential
                ));
            builder.Services.AddTransient<WordPressService>();
            builder.Services.AddTransient<CacheService>(sp => new CacheService(profile.CachePath!, profile.Name));
            builder.Services.AddTransient<WorkspaceService>();

            builder.Services.AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

            var app = builder.Build();

            await app.RunAsync();

            return (int)ExitCode.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MCP server error: {ex}");
            return (int)ExitCode.UnhandledError;
        }
    }
}
