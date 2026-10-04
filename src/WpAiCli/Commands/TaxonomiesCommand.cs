using System;
using System.Threading;
using System.Threading.Tasks;
using WpAiCli.Configuration;
using WpAiCli.Output;
using WpAiCli.Services;

namespace WpAiCli.Commands;

public static class TaxonomiesCommand
{
    public static async Task<int> ExecuteAsync(string[] args, WorkspaceService workspaceService)
    {
        if (args.Length == 0 || (args[0].ToLowerInvariant() != "sync" && args[0].ToLowerInvariant() != "pull"))
        {
            Console.Error.WriteLine("Specify taxonomies subcommand (sync|pull).");
            return (int)ExitCode.InvalidArguments;
        }

        var isPull = args[0].ToLowerInvariant() == "pull";
        Console.WriteLine(isPull ? "Pulling taxonomies from server..." : "Starting two-way taxonomies synchronization...");
        var report = isPull
            ? await workspaceService.PullTaxonomiesAsync(CancellationToken.None)
            : await workspaceService.SyncTaxonomiesAsync(CancellationToken.None);
        OutputFormatter.WriteTransferReport(report, Console.Out);
        return (int)ExitCode.Success;
    }
}
