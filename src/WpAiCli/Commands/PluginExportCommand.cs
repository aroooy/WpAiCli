using System;
using System.IO;
using System.Reflection;
using WpAiCli.Configuration;

namespace WpAiCli.Commands;

public static class PluginExportCommand
{
    public static int Execute()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "WpAiCli.Resources.mu-plugins.zip";
        var outputFileName = "mu-plugins.zip";

        using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
        {
            if (resourceStream == null)
            {
                Console.Error.WriteLine("Error: The plugin resource (mu-plugins.zip) could not be found in the executable.");
                Console.Error.WriteLine("The application might be corrupted. Please try reinstalling.");
                return (int)ExitCode.UnhandledError;
            }

            using var fileStream = new FileStream(outputFileName, FileMode.Create, FileAccess.Write);
            resourceStream.CopyTo(fileStream);
        }

        Console.WriteLine($"Successfully exported '{outputFileName}' to the current directory.");
        Console.WriteLine();
        Console.WriteLine("--- WordPress Plugin Installation ---");
        Console.WriteLine();
        Console.WriteLine("To enable full Markdown sync, the companion mu-plugin must be installed on your WordPress site.");
        Console.WriteLine("Please follow these steps:");
        Console.WriteLine();
        Console.WriteLine("1. Log in to your server via FTP or your hosting provider's file manager.");
        Console.WriteLine("2. Navigate to the 'wp-content' directory of your WordPress installation.");
        Console.WriteLine("3. If it does not already exist, create a new directory named 'mu-plugins'.");
        Console.WriteLine("4. Unzip the 'mu-plugins.zip' file you just exported on your local machine.");
        Console.WriteLine("5. Upload all files and folders from inside the unzipped directory to your server's 'wp-content/mu-plugins/' directory.");
        Console.WriteLine();
        Console.WriteLine("Once the files are in place, WordPress will load them automatically.");

        return (int)ExitCode.Success;
    }
}
