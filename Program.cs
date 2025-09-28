using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using WpAiCli.Configuration;
using WpAiCli.Completion;
using WpAiCli.Help;
using WpAiCli.Output;
using WpAiCli.Parsing;
using WpAiCli.Services;
using WpAiCli.WordPress.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

string? globalConnectionName;
args = ExtractGlobalOptions(args, out globalConnectionName);

if (args.Length == 0)
{
    PrintDocs();
    return (int)ExitCode.Success;
}

if (args.Length == 1 && args[0] is "--help" or "-h" or "help")
{
    PrintDocs();
    return (int)ExitCode.Success;
}

if (args.Length == 1 && args[0] is "--version" or "-V")
{
    var version = typeof(Program).Assembly.GetName().Version;
    Console.WriteLine(version?.ToString() ?? "unknown");
    return (int)ExitCode.Success;
}

var command = args[0].ToLowerInvariant();
var commandArgs = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "posts" => await HandlePostsAsync(commandArgs),
        "categories" => await HandleCategoriesAsync(commandArgs),
        "tags" => await HandleTagsAsync(commandArgs),
        "media" => await HandleMediaAsync(commandArgs),
        "connections" => HandleConnections(commandArgs),
        "completion" => HandleCompletion(commandArgs),
        "docs" => PrintDocsAndReturn(),
        _ => UnknownCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return (int)ExitCode.UnhandledError;
}

int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    return (int)ExitCode.InvalidArguments;
}

void PrintDocs()
{
    if (!HelpPrinter.TryPrintDocumentation(Console.Out))
    {
        Console.Error.WriteLine("Help files not found. Place README.md or HOWTO.md alongside the executable.");
    }
}

int PrintDocsAndReturn()
{
    PrintDocs();
    return (int)ExitCode.Success;
}

async Task<int> HandlePostsAsync(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Specify posts subcommand (list|get|create|update|delete|revisions|revision).");
        return (int)ExitCode.InvalidArguments;
    }

    var subcommand = args[0].ToLowerInvariant();
    var subArgs = args.Skip(1).ToArray();
    var parsed = OptionParser.Parse(subArgs);
    var format = OutputFormatter.ParseFormat(parsed.GetString("format"));

    try
    {
        var (store, profile, token) = ResolveConnection(globalConnectionName);
        var settings = new WordPressSettings(profile.BaseUrl, token);
        using var service = new WordPressService(settings);
        var ct = CancellationToken.None;

        int result;

        switch (subcommand)
        {
            case "list":
            {
                var status = parsed.GetString("status");
                var perPage = parsed.GetInt("per-page") ?? 10;
                perPage = Math.Clamp(perPage, 1, 100);
                var page = parsed.GetInt("page") ?? 1;
                page = Math.Max(page, 1);

                var posts = await service.ListPostsAsync(status, perPage, page, ct).ConfigureAwait(false);
                OutputFormatter.WritePosts(posts, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }

            case "get":
            {
                var id = ResolveId(parsed, defaultValue: null);
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a post ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var post = await service.GetPostAsync(id.Value, ct).ConfigureAwait(false);
                OutputFormatter.WritePost(post, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }

            case "create":
            {
                var title = parsed.GetString("title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    Console.Error.WriteLine("Provide --title.");
                    return (int)ExitCode.InvalidArguments;
                }

                var content = parsed.GetString("content");
                var contentFile = ToFileInfo(parsed.GetString("content-file"));
                var status = parsed.GetString("status") ?? "draft";
                var categories = parsed.GetIntArray("categories");
                var tags = parsed.GetIntArray("tags");
                var featured = parsed.GetInt("featured-media");

                var request = new WordPressCreatePostRequest
                {
                    Title = title,
                    Content = ContentLoader.ReadContent(content, contentFile),
                    Status = status,
                    Categories = categories,
                    Tags = tags,
                    FeaturedMedia = featured
                };

                var post = await service.CreatePostAsync(request, ct).ConfigureAwait(false);
                OutputFormatter.WritePost(post, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }

            case "update":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a post ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var content = parsed.GetString("content");
                var contentFile = ToFileInfo(parsed.GetString("content-file"));
                var categories = parsed.GetIntArray("categories");
                var tags = parsed.GetIntArray("tags");
                var featured = parsed.GetInt("featured-media");

                var request = new WordPressUpdatePostRequest
                {
                    Title = parsed.GetString("title"),
                    Content = ContentLoader.ReadContent(content, contentFile),
                    Status = parsed.GetString("status"),
                    Categories = categories,
                    Tags = tags,
                    FeaturedMedia = featured
                };

                var post = await service.UpdatePostAsync(id.Value, request, ct).ConfigureAwait(false);
                OutputFormatter.WritePost(post, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }

            case "delete":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a post ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var response = await service.DeletePostAsync(id.Value, force, ct).ConfigureAwait(false);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }

            case "revisions":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a post ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var revisions = await service.GetPostRevisionsAsync(id.Value, ct).ConfigureAwait(false);
                OutputFormatter.WriteRevisions(revisions, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }

            case "revision":
            {
                if (parsed.Positionals.Count < 2)
                {
                    Console.Error.WriteLine("Provide a post ID and a revision ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                if (!int.TryParse(parsed.Positionals[0], out var postId) || !int.TryParse(parsed.Positionals[1], out var revisionId))
                {
                    Console.Error.WriteLine("Post ID and revision ID must be integers.");
                    return (int)ExitCode.InvalidArguments;
                }

                var revision = await service.GetPostRevisionAsync(postId, revisionId, ct).ConfigureAwait(false);
                OutputFormatter.WriteRevision(revision, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }

            default:
                Console.Error.WriteLine($"Unknown posts subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }

        UpdateLastUsedConnection(store, profile.Name);
        return result;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return (int)ExitCode.InvalidArguments;
    }
    catch (WpAiCli.WordPress.WordPressApiException ex)
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

async Task<int> HandleCategoriesAsync(string[] args)
{
    if (args.Length == 0)
    {
        args = new[] { "list" };
    }

    var subcommand = args[0].ToLowerInvariant();
    var subArgs = args.Skip(1).ToArray();
    var parsed = OptionParser.Parse(subArgs);
    var format = OutputFormatter.ParseFormat(parsed.GetString("format"));

    try
    {
        var (store, profile, token) = ResolveConnection(globalConnectionName);
        var settings = new WordPressSettings(profile.BaseUrl, token);
        using var service = new WordPressService(settings);
        var ct = CancellationToken.None;
        int result;

        switch (subcommand)
        {
            case "list":
            {
                var categories = await service.ListCategoriesAsync(ct).ConfigureAwait(false);
                OutputFormatter.WriteCategories(categories, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }
            case "get":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a category ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var category = await service.GetCategoryAsync(id.Value, ct).ConfigureAwait(false);
                OutputFormatter.WriteCategory(category, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }
            case "delete":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a category ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var response = await service.DeleteCategoryAsync(id.Value, force, ct).ConfigureAwait(false);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }
            default:
                Console.Error.WriteLine($"Unknown categories subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }

        UpdateLastUsedConnection(store, profile.Name);
        return result;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return (int)ExitCode.InvalidArguments;
    }
    catch (WpAiCli.WordPress.WordPressApiException ex)
    {
        Console.Error.WriteLine(ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
        {
            Console.Error.WriteLine(ex.ResponseBody);
        }
        return (int)ExitCode.ApiError;
    }
}

async Task<int> HandleTagsAsync(string[] args)
{
    if (args.Length == 0)
    {
        args = new[] { "list" };
    }

    var subcommand = args[0].ToLowerInvariant();
    var subArgs = args.Skip(1).ToArray();
    var parsed = OptionParser.Parse(subArgs);
    var format = OutputFormatter.ParseFormat(parsed.GetString("format"));

    try
    {
        var (store, profile, token) = ResolveConnection(globalConnectionName);
        var settings = new WordPressSettings(profile.BaseUrl, token);
        using var service = new WordPressService(settings);
        var ct = CancellationToken.None;
        int result;

        switch (subcommand)
        {
            case "list":
            {
                var tags = await service.ListTagsAsync(ct).ConfigureAwait(false);
                OutputFormatter.WriteTags(tags, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }
            case "create":
            {
                var name = parsed.GetString("name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.Error.WriteLine("Provide --name.");
                    return (int)ExitCode.InvalidArguments;
                }

                var request = new WordPressCreateTagRequest
                {
                    Name = name,
                    Slug = parsed.GetString("slug"),
                    Description = parsed.GetString("description")
                };

                var tag = await service.CreateTagAsync(request, ct).ConfigureAwait(false);
                OutputFormatter.WriteTag(tag, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }
            case "get":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a tag ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var tag = await service.GetTagAsync(id.Value, ct).ConfigureAwait(false);
                OutputFormatter.WriteTag(tag, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }
            case "delete":
            {
                var id = ResolveId(parsed, defaultValue: parsed.Positionals.FirstOrDefault());
                if (id is null)
                {
                    Console.Error.WriteLine("Provide a tag ID.");
                    return (int)ExitCode.InvalidArguments;
                }

                var force = parsed.GetBool("force", defaultValue: true);
                var response = await service.DeleteTagAsync(id.Value, force, ct).ConfigureAwait(false);
                OutputFormatter.WriteDeleteResponse(response, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }
            default:
                Console.Error.WriteLine($"Unknown tags subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }

        UpdateLastUsedConnection(store, profile.Name);
        return result;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return (int)ExitCode.InvalidArguments;
    }
    catch (WpAiCli.WordPress.WordPressApiException ex)
    {
        Console.Error.WriteLine(ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
        {
            Console.Error.WriteLine(ex.ResponseBody);
        }
        return (int)ExitCode.ApiError;
    }
}

async Task<int> HandleMediaAsync(string[] args)
{
    if (args.Length == 0)
    {
        args = new[] { "list" };
    }

    var subcommand = args[0].ToLowerInvariant();
    var subArgs = args.Skip(1).ToArray();
    var parsed = OptionParser.Parse(subArgs);
    var format = OutputFormatter.ParseFormat(parsed.GetString("format"));

    try
    {
        var (store, profile, token) = ResolveConnection(globalConnectionName);
        var settings = new WordPressSettings(profile.BaseUrl, token);
        using var service = new WordPressService(settings);
        var ct = CancellationToken.None;
        int result;

        switch (subcommand)
        {
            case "list":
            {
                var perPage = parsed.GetInt("per-page") ?? 10;
                perPage = Math.Clamp(perPage, 1, 100);
                var page = parsed.GetInt("page") ?? 1;
                page = Math.Max(page, 1);

                var mediaItems = await service.ListMediaAsync(perPage, page, ct).ConfigureAwait(false);
                OutputFormatter.WriteMediaItems(mediaItems, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }
            case "upload":
            {
                var filePath = parsed.Positionals.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    Console.Error.WriteLine("Provide a file path to upload.");
                    return (int)ExitCode.InvalidArguments;
                }

                var title = parsed.GetString("title");
                var description = parsed.GetString("description");

                var mediaItem = await service.UploadMediaAsync(filePath, title, description, ct).ConfigureAwait(false);
                OutputFormatter.WriteMediaItem(mediaItem, format, Console.Out);
                result = (int)ExitCode.Success;
                break;
            }
            default:
                Console.Error.WriteLine($"Unknown media subcommand: {subcommand}");
                return (int)ExitCode.InvalidArguments;
        }

        UpdateLastUsedConnection(store, profile.Name);
        return result;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return (int)ExitCode.InvalidArguments;
    }
    catch (WpAiCli.WordPress.WordPressApiException ex)
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

int HandleConnections(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Specify connections subcommand (list|add|remove).");
        return (int)ExitCode.InvalidArguments;
    }

    var subcommand = args[0].ToLowerInvariant();
    var subArgs = args.Skip(1).ToArray();
    var parsed = OptionParser.Parse(subArgs);

    return subcommand switch
    {
        "list" => HandleConnectionsList(),
        "add" => HandleConnectionsAdd(parsed),
        "remove" => HandleConnectionsRemove(),
        _ => UnknownConnectionsCommand(subcommand)
    };
}

int UnknownConnectionsCommand(string subcommand)
{
    Console.Error.WriteLine($"Unknown connections subcommand: {subcommand}");
    return (int)ExitCode.InvalidArguments;
}

int HandleConnectionsList()
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
        var isLastUsed = string.Equals(profile.Name, store.LastUsedConnection, StringComparison.OrdinalIgnoreCase);
        var marker = isLastUsed ? "*" : " ";
        Console.WriteLine($" {marker} {i + 1}. {profile.Name} ({profile.BaseUrl})");
    }

    if (!string.IsNullOrWhiteSpace(store.LastUsedConnection))
    {
        Console.WriteLine($"* indicates the last used connection ({store.LastUsedConnection}).");
    }

    return (int)ExitCode.Success;
}

int HandleConnectionsAdd(ParsedOptions parsed)
{
    var name = parsed.GetString("name");
    var baseUrl = parsed.GetString("base-url");
    var token = parsed.GetString("token");

    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
    {
        Console.Error.WriteLine("Provide --name, --base-url, and --token when adding a connection.");
        return (int)ExitCode.InvalidArguments;
    }

    var store = ConnectionStore.Load();
    if (store.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine($"Connection '{name}' already exists. Remove it first if you need to redefine it.");
        return (int)ExitCode.InvalidArguments;
    }

    var profile = new ConnectionProfile
    {
        Name = name,
        BaseUrl = baseUrl.Trim(),
        CredentialKey = $"WpAiCli/{name}"
    };

    CredentialManager.Save(profile.CredentialKey, token);
    store.Profiles.Add(profile);
    store.LastUsedConnection = profile.Name;
    store.Save();

    Console.WriteLine($"Connection '{profile.Name}' registered.");
    return (int)ExitCode.Success;
}

int HandleConnectionsRemove()
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

int HandleCompletion(string[] args)
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

string[] ExtractGlobalOptions(string[] source, out string? connectionOverride)
{
    connectionOverride = null;
    var remaining = new List<string>();

    for (int i = 0; i < source.Length; i++)
    {
        var token = source[i];
        if (!token.StartsWith("--"))
        {
            remaining.Add(token);
            continue;
        }

        if (TryMatchOption(token, "connection", out var inlineValue))
        {
            connectionOverride = ExtractValue(source, ref i, inlineValue);
            continue;
        }

        remaining.Add(token);
    }

    connectionOverride = string.IsNullOrWhiteSpace(connectionOverride) ? null : connectionOverride.Trim();

    return remaining.ToArray();

    static bool TryMatchOption(string token, string optionName, out string? inlineValue)
    {
        var prefix = $"--{optionName}";
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            inlineValue = null;
            return false;
        }

        if (token.Length == prefix.Length)
        {
            inlineValue = null;
            return true;
        }

        if (token[prefix.Length] == '=')
        {
            inlineValue = token[(prefix.Length + 1)..];
            return true;
        }

        inlineValue = null;
        return false;
    }

    static string? ExtractValue(string[] source, ref int index, string? inlineValue)
    {
        if (inlineValue is not null)
        {
            return inlineValue;
        }

        if (index + 1 < source.Length && !source[index + 1].StartsWith("--"))
        {
            index++;
            return source[index];
        }

        return null;
    }
}

(ConnectionStore Store, ConnectionProfile Profile, string Token) ResolveConnection(string? requestedName)
{
    var store = ConnectionStore.Load();
    if (store.Profiles.Count == 0)
    {
        throw new InvalidOperationException("No connections registered. Use `wpai connections add` first.");
    }

    ConnectionProfile? profile = null;

    if (!string.IsNullOrWhiteSpace(requestedName))
    {
        profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, requestedName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            throw new InvalidOperationException($"Connection '{requestedName}' was not found. Run `wpai connections list` to review names.");
        }
    }
    else if (!string.IsNullOrWhiteSpace(store.LastUsedConnection))
    {
        profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, store.LastUsedConnection, StringComparison.OrdinalIgnoreCase));
    }

    profile ??= store.Profiles.Count == 1
        ? store.Profiles[0]
        : throw new InvalidOperationException("Multiple connections registered. Specify one with `--connection <name>`.");

    var token = CredentialManager.ReadSecret(profile.CredentialKey);
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new InvalidOperationException($"Credential for connection '{profile.Name}' is missing. Re-add the connection.");
    }

    return (store, profile, token);
}

void UpdateLastUsedConnection(ConnectionStore store, string connectionName)
{
    if (!string.Equals(store.LastUsedConnection, connectionName, StringComparison.OrdinalIgnoreCase))
    {
        store.LastUsedConnection = connectionName;
        store.Save();
    }
}

int? ResolveId(ParsedOptions parsed, string? defaultValue)
{
    if (parsed.Positionals.Count > 0 && int.TryParse(parsed.Positionals[0], out var positionalId))
    {
        return positionalId;
    }

    if (!string.IsNullOrWhiteSpace(defaultValue) && int.TryParse(defaultValue, out var fallback))
    {
        return fallback;
    }

    return parsed.GetInt("id");
}

FileInfo? ToFileInfo(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return null;
    }

    return new FileInfo(path);
}








