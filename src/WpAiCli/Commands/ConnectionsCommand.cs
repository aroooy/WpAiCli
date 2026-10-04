using System;
using System.IO;
using System.Linq;
using WpAiCli.Configuration;
using WpAiCli.Parsing;

namespace WpAiCli.Commands;

public static class ConnectionsCommand
{
    public static int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Specify connections subcommand (list|add|update|remove|active).");
            return (int)ExitCode.InvalidArguments;
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();
        var parsed = OptionParser.Parse(subArgs);

        return subcommand switch
        {
            "list" => HandleList(),
            "add" => HandleAdd(parsed),
            "update" => HandleUpdate(parsed),
            "remove" => HandleRemove(),
            "active" => HandleActive(parsed),
            _ => UnknownCommand(subcommand)
        };
    }

    private static int UnknownCommand(string subcommand)
    {
        Console.Error.WriteLine($"Unknown connections subcommand: {subcommand}");
        return (int)ExitCode.InvalidArguments;
    }

    public static int HandleList()
    {
        var store = ConnectionStore.Load();
        if (store.Profiles.Count == 0)
        {
            Console.WriteLine("No connections have been registered yet.");
            return (int)ExitCode.Success;
        }

        Console.WriteLine("Registered connections:");
        for (int i = 0; i < store.Profiles.Count; i++)
        {
            var profile = store.Profiles[i];
            var isActive = string.Equals(profile.Name, store.ActiveConnection, StringComparison.OrdinalIgnoreCase);

            string prefix = isActive ? "=>" : "  ";
            Console.WriteLine($"{prefix} {i + 1}. {profile.Name} ({profile.BaseUrl})");
        }

        Console.WriteLine();
        if (!string.IsNullOrWhiteSpace(store.ActiveConnection))
        {
            Console.WriteLine($"=> indicates the active connection ({store.ActiveConnection}).");
        }

        return (int)ExitCode.Success;
    }

    private static int HandleActive(ParsedOptions parsed)
    {
        var store = ConnectionStore.Load();
        var argument = parsed.Positionals.FirstOrDefault();

        ConnectionProfile? profile = null;

        if (!string.IsNullOrWhiteSpace(argument))
        {
            if (int.TryParse(argument, out var choice) && choice >= 1 && choice <= store.Profiles.Count)
            {
                profile = store.Profiles[choice - 1];
            }
            else
            {
                profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, argument, StringComparison.OrdinalIgnoreCase));
                if (profile is null)
                {
                    Console.Error.WriteLine($"Connection '{argument}' not found.");
                    return (int)ExitCode.InvalidArguments;
                }
            }
        }
        else // Interactive mode
        {
            if (store.Profiles.Count == 0)
            {
                Console.WriteLine("No connections have been registered yet.");
                return (int)ExitCode.Success;
            }

            Console.WriteLine("Select the connection to set as active:");
            for (int i = 0; i < store.Profiles.Count; i++)
            {
                var p = store.Profiles[i];
                var isActive = string.Equals(p.Name, store.ActiveConnection, StringComparison.OrdinalIgnoreCase);
                var prefix = isActive ? "=>" : "  ";
                Console.WriteLine($"{prefix} {i + 1}. {p.Name}");
            }

            Console.Write("\nEnter number to set active (blank to cancel): ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            { 
                Console.WriteLine("Operation cancelled.");
                return (int)ExitCode.Success;
            }

            if (!int.TryParse(input, out var choice) || choice < 1 || choice > store.Profiles.Count)
            {
                Console.Error.WriteLine("Invalid selection.");
                return (int)ExitCode.InvalidArguments;
            }
            profile = store.Profiles[choice - 1];
        }

        if (profile is null)
        {
            Console.Error.WriteLine("Could not determine a connection to activate.");
            return (int)ExitCode.InvalidArguments;
        }

        store.ActiveConnection = profile.Name;
        store.Save();

        Console.WriteLine($"Active connection set to: {profile.Name}");
        return (int)ExitCode.Success;
    }

    private static int HandleAdd(ParsedOptions parsed)
    {
        var name = parsed.GetString("name");
        var baseUrl = parsed.GetString("base-url");
        var authMethod = parsed.GetString("auth-method") ?? "ApplicationPassword";
        var cachePath = parsed.GetString("cache-path");
        var syncLimitStr = parsed.GetString("sync-limit");

        if (string.IsNullOrWhiteSpace(cachePath))
        {
            cachePath = Path.Combine(Directory.GetCurrentDirectory(), "wp-cache");
        }

        var markdownConversion = parsed.GetString("markdown-conversion");

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl))
        {
            Console.Error.WriteLine("Provide --name and --base-url when adding a connection.");
            return (int)ExitCode.InvalidArguments;
        }

        if (authMethod != "ApplicationPassword" && authMethod != "Jwt")
        {
            Console.Error.WriteLine("Invalid value for --auth-method. Must be 'ApplicationPassword' or 'Jwt'.");
            return (int)ExitCode.InvalidArguments;
        }
        
        string credential;
        string? userName = null;

        if (authMethod == "ApplicationPassword")
        {
            userName = parsed.GetString("username");
            var password = parsed.GetString("password");
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                Console.Error.WriteLine("For ApplicationPassword authentication, provide --username and --password.");
                return (int)ExitCode.InvalidArguments;
            }
            credential = password;
        }
        else // Jwt
        {
            var jwtToken = parsed.GetString("jwt-token");
            if (string.IsNullOrWhiteSpace(jwtToken))
            {
                Console.Error.WriteLine("For Jwt authentication, provide --jwt-token.");
                return (int)ExitCode.InvalidArguments;
            }
            credential = jwtToken;
        }

        if (markdownConversion != null && markdownConversion != "client" && markdownConversion != "server")
        {
            Console.Error.WriteLine("Invalid value for --markdown-conversion. Must be 'client' or 'server'.");
            return (int)ExitCode.InvalidArguments;
        }

        var store = ConnectionStore.Load();
        bool isFirstConnection = store.Profiles.Count == 0;

        if (store.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"Connection '{name}' already exists. Remove it first if you need to redefine it.");
            return (int)ExitCode.InvalidArguments;
        }

        var absoluteCachePath = !string.IsNullOrWhiteSpace(cachePath) ? Path.GetFullPath(cachePath) : null;
        int? syncLimit = int.TryParse(syncLimitStr, out var parsedLimit) ? parsedLimit : null;

        var profile = new ConnectionProfile
        {
            Name = name,
            BaseUrl = baseUrl.Trim(),
            CredentialKey = $"WpAiCli/{name}",
            AuthMethod = authMethod,
            UserName = userName,
            CachePath = absoluteCachePath,
            SyncItemsLimit = syncLimit,
            MarkdownConversion = markdownConversion
        };

        CredentialManager.Save(profile.CredentialKey, credential);
        store.Profiles.Add(profile);
        store.LastUsedConnection = profile.Name;

        if (isFirstConnection)
        {
            store.ActiveConnection = profile.Name;
        }

        store.Save();

        Console.WriteLine($"Connection '{profile.Name}' registered with '{profile.AuthMethod}' authentication.");

        if (isFirstConnection)
        {
            Console.WriteLine("As this is the first connection, it has been set as the active connection.");
        }

        if (profile.CachePath is not null)
        {
            Console.WriteLine($"Cache location: {profile.CachePath}");
        }
        if (profile.SyncItemsLimit is not null)
        {
            Console.WriteLine($"Sync limit: {profile.SyncItemsLimit}");
        }
        return (int)ExitCode.Success;
    }

    private static int HandleUpdate(ParsedOptions parsed)
    {
        if (parsed.GetBool("help", defaultValue: false) || parsed.GetBool("h", defaultValue: false))
        {
            Console.WriteLine("Usage: wpai connections update <name> [options]");
            Console.WriteLine("\nUpdates an existing connection.");
            Console.WriteLine("\nArguments:");
            Console.WriteLine("  <name>             The name of the connection to update.");
            Console.WriteLine("\nOptions:");
            Console.WriteLine("  --base-url <url>   Update the WordPress site URL.");
            Console.WriteLine("  --auth-method <method>");
            Console.WriteLine("                     Update the authentication method (ApplicationPassword|Jwt).");
            Console.WriteLine("  --username <user>  Update the username for Application Password auth.");
            Console.WriteLine("  --password <pass>  Update the Application Password.");
            Console.WriteLine("  --jwt-token <token>  Update the JWT token.");
            Console.WriteLine("  --cache-path <path> Update the local cache directory path.");
            Console.WriteLine("  --sync-limit <num> Update the number of items to fetch during sync.");
            Console.WriteLine("  --markdown-conversion <mode>");
            Console.WriteLine("                     Update the Markdown conversion mode (client|server).");
            Console.WriteLine("  --help, -h         Show this help message.");
            return (int)ExitCode.Success;
        }

        var name = parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Specify the name of the connection to update.");
            Console.Error.WriteLine("Use 'wpai connections update --help' for more information.");
            return (int)ExitCode.InvalidArguments;
        }

        var store = ConnectionStore.Load();
        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            Console.Error.WriteLine($"Connection '{name}' not found.");
            return (int)ExitCode.InvalidArguments;
        }

        // Non-interactive mode check
        if (parsed.HasOptions)
        {
            return HandleUpdateNonInteractive(parsed, profile, store);
        }

        // Interactive mode
        while (true)
        {
            Console.WriteLine($"\nUpdating connection: {profile.Name} ({profile.BaseUrl})");
            Console.WriteLine("What do you want to update?");
            Console.WriteLine("  1: Base URL");
            Console.WriteLine("  2: Authentication");
            Console.WriteLine("  3: Cache Path");
            Console.WriteLine("  4: Sync Items Limit");
            Console.WriteLine("  5: Markdown Conversion");
            Console.WriteLine("  q: Quit");
            Console.Write("Enter your choice: ");

            var choice = Console.ReadLine();
            bool shouldQuit = false;

            switch (choice?.ToLower())
            {
                case "1":
                    Console.Write($"Enter new Base URL (current: {profile.BaseUrl}): ");
                    var newUrl = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(newUrl))
                    {
                        profile.BaseUrl = newUrl.TrimEnd('/');
                        store.Save();
                        Console.WriteLine("Base URL updated successfully.");
                    }
                    break;
                case "2":
                    UpdateAuthenticationInteractive(profile, store);
                    break;
                case "3":
                    Console.Write($"Enter new Cache Path (current: {profile.CachePath ?? "Not set"}): ");
                    var newCachePath = Console.ReadLine();
                    profile.CachePath = !string.IsNullOrWhiteSpace(newCachePath) ? Path.GetFullPath(newCachePath) : null;
                    store.Save();
                    Console.WriteLine("Cache Path updated successfully.");
                    break;
                case "4":
                    Console.Write($"Enter new Sync Items Limit (current: {profile.SyncItemsLimit?.ToString() ?? "Default"}): ");
                    var newSyncLimitStr = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(newSyncLimitStr))
                    {
                        profile.SyncItemsLimit = null;
                        Console.WriteLine("Sync limit has been reset to default.");
                    }
                    else if (int.TryParse(newSyncLimitStr, out var newSyncLimit) && newSyncLimit > 0)
                    {
                        profile.SyncItemsLimit = newSyncLimit;
                        Console.WriteLine($"Sync limit set to: {newSyncLimit}");
                    }
                    else
                    {
                        Console.Error.WriteLine("Invalid input. Must be a positive integer.");
                    }
                    store.Save();
                    break;
                case "5":
                    Console.Write($"Enter new Markdown Conversion (client/server, current: {profile.MarkdownConversion ?? "Default"}): ");
                    var newMarkdownConversion = Console.ReadLine()?.ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(newMarkdownConversion))
                    {
                        profile.MarkdownConversion = null;
                        Console.WriteLine("Markdown conversion has been reset to default.");
                    }
                    else if (newMarkdownConversion == "client" || newMarkdownConversion == "server")
                    {
                        profile.MarkdownConversion = newMarkdownConversion;
                        Console.WriteLine($"Markdown conversion set to: {newMarkdownConversion}");
                    }
                    else
                    {
                        Console.Error.WriteLine("Invalid input. Must be 'client' or 'server'.");
                    }
                    store.Save();
                    break;
                case "q":
                    shouldQuit = true;
                    break;
                default:
                    Console.WriteLine("Invalid choice. Please try again.");
                    break;
            }
            if (shouldQuit) break;
        }
        return (int)ExitCode.Success;
    }

    private static void UpdateAuthenticationInteractive(ConnectionProfile profile, ConnectionStore store)
    {
        Console.WriteLine($"\nCurrent authentication method: {profile.AuthMethod}");
        Console.Write("Choose new method (ApplicationPassword/Jwt) or press Enter to keep current: ");
        var newAuthMethod = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(newAuthMethod))
        {
            newAuthMethod = profile.AuthMethod;
        }

        if (string.Equals(newAuthMethod, "ApplicationPassword", StringComparison.OrdinalIgnoreCase))
        {
            profile.AuthMethod = "ApplicationPassword";
            Console.Write($"Enter new Username (current: {profile.UserName}): ");
            var newUsername = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(newUsername)) 
            {
                profile.UserName = newUsername;
            }
            else if (string.IsNullOrWhiteSpace(profile.UserName))
            {
                Console.Error.WriteLine("Username cannot be empty for Application Password authentication.");
                return;
            }

            Console.Write("Enter new Application Password: ");
            var newPassword = CommandHelpers.ReadPassword();
            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                CredentialManager.Save(profile.CredentialKey, newPassword);
                Console.WriteLine("Application Password updated successfully.");
            }
        }
        else if (string.Equals(newAuthMethod, "Jwt", StringComparison.OrdinalIgnoreCase))
        {
            profile.AuthMethod = "Jwt";
            profile.UserName = null;
            Console.Write("Enter new JWT Token: ");
            var newJwt = CommandHelpers.ReadPassword();
            if (!string.IsNullOrWhiteSpace(newJwt))
            {
                CredentialManager.Save(profile.CredentialKey, newJwt);
                Console.WriteLine("JWT Token updated successfully.");
            }
        }
        else
        {
            Console.Error.WriteLine("Invalid authentication method.");
            return;
        }
        store.Save();
    }

    private static int HandleUpdateNonInteractive(ParsedOptions parsed, ConnectionProfile profile, ConnectionStore store)
    {
        var updated = false;

        var baseUrl = parsed.GetString("base-url");
        if (baseUrl != null)
        {
            profile.BaseUrl = baseUrl.TrimEnd('/');
            updated = true;
            Console.WriteLine($"Base URL set to: {profile.BaseUrl}");
        }

        var authMethod = parsed.GetString("auth-method");
        if (authMethod != null)
        {
            if (authMethod == "ApplicationPassword" || authMethod == "Jwt")
            {
                profile.AuthMethod = authMethod;
                updated = true;
                Console.WriteLine($"Authentication method set to: {profile.AuthMethod}");
            }
            else
            {
                Console.Error.WriteLine("Invalid auth-method. Must be 'ApplicationPassword' or 'Jwt'.");
                return (int)ExitCode.InvalidArguments;
            }
        }

        var username = parsed.GetString("username");
        if (username != null)
        {
            profile.UserName = username;
            updated = true;
            Console.WriteLine($"Username set to: {profile.UserName}");
        }

        var password = parsed.GetString("password");
        if (password != null)
        {
            CredentialManager.Save(profile.CredentialKey, password);
            updated = true;
            Console.WriteLine("Password updated.");
        }

        var jwtToken = parsed.GetString("jwt-token");
        if (jwtToken != null)
        {
            CredentialManager.Save(profile.CredentialKey, jwtToken);
            updated = true;
            Console.WriteLine("JWT Token updated.");
        }

        var cachePath = parsed.GetString("cache-path");
        if (cachePath is not null)
        {
            profile.CachePath = !string.IsNullOrWhiteSpace(cachePath) ? Path.GetFullPath(cachePath) : null;
            updated = true;
            Console.WriteLine(profile.CachePath is not null
                ? $"Cache location set to: {profile.CachePath}"
                : "Cache location has been removed.");
        }

        var syncLimitStr = parsed.GetString("sync-limit");
        if (syncLimitStr is not null)
        {
            if (int.TryParse(syncLimitStr, out var parsedLimit) && parsedLimit > 0)
            {
                profile.SyncItemsLimit = parsedLimit;
                Console.WriteLine($"Sync limit set to: {profile.SyncItemsLimit}");
            }
            else if (string.IsNullOrEmpty(syncLimitStr))
            {
                profile.SyncItemsLimit = null;
                Console.WriteLine("Sync limit has been reset to default (30).");
            }
            else
            {
                Console.Error.WriteLine($"Invalid value for --sync-limit: '{syncLimitStr}'. Must be a positive integer.");
                return (int)ExitCode.InvalidArguments;
            }
            updated = true;
        }

        var markdownConversion = parsed.GetString("markdown-conversion");
        if (markdownConversion is not null)
        {
            if (markdownConversion == "client" || markdownConversion == "server")
            {
                profile.MarkdownConversion = markdownConversion;
                Console.WriteLine($"Markdown conversion strategy set to: {profile.MarkdownConversion}");
            }
            else if (string.IsNullOrEmpty(markdownConversion))
            {
                profile.MarkdownConversion = null;
                Console.WriteLine("Markdown conversion strategy has been reset to default (client).");
            }
            else
            {
                Console.Error.WriteLine($"Invalid value for --markdown-conversion: '{markdownConversion}'. Must be 'client' or 'server'.");
                return (int)ExitCode.InvalidArguments;
            }
            updated = true;
        }

        if (updated)
        {
            store.Save();
            Console.WriteLine($"\nConnection '{profile.Name}' updated.");
        }

        return (int)ExitCode.Success;
    }

    private static int HandleRemove()
    {
        var store = ConnectionStore.Load();
        if (store.Profiles.Count == 0)
        {
            Console.WriteLine("No connections available to remove.");
            return (int)ExitCode.Success;
        }

        Console.WriteLine("Select the connection to remove:");
        for (int i = 0; i < store.Profiles.Count; i++)
        {
            Console.WriteLine($" {i + 1}. {store.Profiles[i].Name}");
        }

        Console.Write("Enter number (blank to cancel): ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine("Removal cancelled.");
            return (int)ExitCode.Success;
        }

        if (!int.TryParse(input, out var choice) || choice < 1 || choice > store.Profiles.Count)
        {
            Console.Error.WriteLine("Invalid selection.");
            return (int)ExitCode.InvalidArguments;
        }

        var profile = store.Profiles[choice - 1];
        Console.Write($"Delete connection '{profile.Name}'? (y/N): ");
        var confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Removal cancelled.");
            return (int)ExitCode.Success;
        }

        CredentialManager.Delete(profile.CredentialKey);
        store.Profiles.RemoveAt(choice - 1);
        if (string.Equals(store.LastUsedConnection, profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            store.LastUsedConnection = store.Profiles.FirstOrDefault()?.Name;
        }

        store.Save();
        Console.WriteLine("Connection removed.");
        return (int)ExitCode.Success;
    }
}
