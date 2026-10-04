using System;
using System.IO;
using WpAiCli.Configuration;

namespace WpAiCli.Commands;

public static class CacheCommand
{
    public static int Execute(string[] args)
    {
        try
        {
            var (store, profile, credential) = CommandHelpers.ResolveConnection();
            if (string.IsNullOrWhiteSpace(profile.CachePath) || string.IsNullOrWhiteSpace(profile.Name))
            {
                Console.Error.WriteLine("Cache path is not configured for the active connection.");
                return (int)ExitCode.InvalidArguments;
            }

            var cacheRoot = Path.Combine(profile.CachePath!, profile.Name);
            Console.WriteLine(cacheRoot);
            return (int)ExitCode.Success;
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
}
