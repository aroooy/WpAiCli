using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WpAiCli.Configuration;
using WpAiCli.Parsing;
using WpAiCli.Services;

namespace WpAiCli.Commands;

public static class ResolveCommand
{
    public static async Task<int> ExecuteAsync(
        string[] args,
        WorkspaceService workspaceService,
        ConnectionProfile profile)
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

        await workspaceService.ResolveConflictAsync(type, id, strategy, profile, CancellationToken.None);
        return (int)ExitCode.Success;
    }
}
