using System;
using System.Linq;
using WpAiCli.Completion;
using WpAiCli.Configuration;
using WpAiCli.Parsing;

namespace WpAiCli.Commands;

public static class CompletionCommand
{
    public static int Execute(string[] args)
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
}
