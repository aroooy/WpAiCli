using System.Text.Json;
using System.Text.Json.Serialization;
using WpAiCli.WordPress.Models;

namespace WpAiCli.Output;

public enum OutputFormat
{
    Table,
    Json,
    Raw
}

public static class OutputFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static OutputFormat ParseFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OutputFormat.Table;
        }

        return value.ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            "raw" => OutputFormat.Raw,
            _ => OutputFormat.Table
        };
    }

    public static void WritePosts(IEnumerable<WordPressPostBase> posts, OutputFormat format, TextWriter writer)
    {
        var postList = posts.ToList();
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(postList, SerializerOptions));
                break;
            case OutputFormat.Raw:
                foreach (var post in postList)
                {
                    writer.WriteLine($"{post.Id}\t{post.Status}\t{Truncate(GetTitleText(post), 80)}");
                }
                break;
            default:
                WriteTable(writer,
                    new[] { "ID", "Status", "Title", "Modified" },
                    postList.Select(p => new[]
                    {
                        p.Id.ToString(),
                        p.Status ?? string.Empty,
                        Truncate(GetTitleText(p), 60),
                        p.Modified?.ToString("u") ?? string.Empty
                    }));
                break;
        }
    }

    public static void WritePost(WordPressPostDetail post, OutputFormat format, TextWriter writer)
    {
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(post, SerializerOptions));
                break;
            case OutputFormat.Raw:
                writer.WriteLine($"ID: {post.Id}");
                writer.WriteLine($"Status: {post.Status}");
                writer.WriteLine($"Title: {GetTitleText(post)}");
                writer.WriteLine($"Link: {post.Link}");
                writer.WriteLine("--- CONTENT ---");
                writer.WriteLine(post.Content?.Rendered ?? post.Content?.Raw ?? string.Empty);
                break;
            default:
                WriteTable(writer,
                    new[] { "Field", "Value" },
                    new[]
                    {
                        new[] { "ID", post.Id.ToString() },
                        new[] { "Status", post.Status ?? string.Empty },
                        new[] { "Title", GetTitleText(post) },
                        new[] { "Author", post.Author?.ToString() ?? string.Empty },
                        new[] { "Modified", post.Modified?.ToString("u") ?? string.Empty },
                        new[] { "Link", post.Link ?? string.Empty },
                        new[] { "Categories", post.Categories is { Count: > 0 } c ? string.Join(",", c) : string.Empty },
                        new[] { "Tags", post.Tags is { Count: > 0 } t ? string.Join(",", t) : string.Empty },
                        new[] { "FeaturedMedia", post.FeaturedMedia?.ToString() ?? string.Empty }
                    });
                writer.WriteLine();
                writer.WriteLine("--- Content (Rendered) ---");
                writer.WriteLine(post.Content?.Rendered ?? post.Content?.Raw ?? string.Empty);
                break;
        }
    }

    public static void WriteCategories(IEnumerable<WordPressCategory> categories, OutputFormat format, TextWriter writer)
    {
        var list = categories.ToList();
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(list, SerializerOptions));
                break;
            case OutputFormat.Raw:
                foreach (var cat in list)
                {
                    writer.WriteLine($"{cat.Id}\t{cat.Name}\t{cat.Slug}");
                }
                break;
            default:
                WriteTable(writer,
                    new[] { "ID", "Name", "Slug", "Posts" },
                    list.Select(cat => new[]
                    {
                        cat.Id.ToString(),
                        Truncate(cat.Name, 40),
                        Truncate(cat.Slug, 40),
                        cat.Count?.ToString() ?? string.Empty
                    }));
                break;
        }
    }

    public static void WriteCategory(WordPressCategory category, OutputFormat format, TextWriter writer)
    {
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(category, SerializerOptions));
                break;
            case OutputFormat.Raw:
                writer.WriteLine($"{category.Id}\t{category.Name}\t{category.Slug}");
                break;
            default:
                WriteTable(writer,
                    new[] { "Field", "Value" },
                    new[]
                    {
                        new[] { "ID", category.Id.ToString() },
                        new[] { "Name", category.Name ?? string.Empty },
                        new[] { "Slug", category.Slug ?? string.Empty },
                        new[] { "Description", category.Description ?? string.Empty },
                        new[] { "Count", category.Count?.ToString() ?? string.Empty },
                        new[] { "Parent", category.Parent?.ToString() ?? string.Empty },
                    });
                break;
        }
    }

    public static void WriteTags(IEnumerable<WordPressTag> tags, OutputFormat format, TextWriter writer)
    {
        var list = tags.ToList();
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(list, SerializerOptions));
                break;
            case OutputFormat.Raw:
                foreach (var tag in list)
                {
                    writer.WriteLine($"{tag.Id}\t{tag.Name}\t{tag.Slug}");
                }
                break;
            default:
                WriteTable(writer,
                    new[] { "ID", "Name", "Slug", "Posts" },
                    list.Select(tag => new[]
                    {
                        tag.Id.ToString(),
                        Truncate(tag.Name, 40),
                        Truncate(tag.Slug, 40),
                        tag.Count?.ToString() ?? string.Empty
                    }));
                break;
        }
    }

    public static void WriteTag(WordPressTag tag, OutputFormat format, TextWriter writer)
    {
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(tag, SerializerOptions));
                break;
            case OutputFormat.Raw:
                writer.WriteLine($"{tag.Id}\t{tag.Name}\t{tag.Slug}");
                break;
            default:
                WriteTable(writer,
                    new[] { "Field", "Value" },
                    new[]
                    {
                        new[] { "ID", tag.Id.ToString() },
                        new[] { "Name", tag.Name ?? string.Empty },
                        new[] { "Slug", tag.Slug ?? string.Empty },
                        new[] { "Description", tag.Description ?? string.Empty },
                        new[] { "Count", tag.Count?.ToString() ?? string.Empty },
                    });
                break;
        }
    }

    public static void WriteDeleteResponse(WordPressDeleteResponse response, OutputFormat format, TextWriter writer)
    {
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(response, SerializerOptions));
                break;
            case OutputFormat.Raw:
                writer.WriteLine(response.Deleted ? "deleted" : "not deleted");
                break;
            default:
                WriteTable(writer,
                    new[] { "Deleted", "PreviousID", "PreviousTitle" },
                    new[]
                    {
                        new[]
                        {
                            response.Deleted.ToString(),
                            response.Previous is { } prev && prev.TryGetValue("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number
                                ? idElement.GetInt32().ToString()
                                : string.Empty,
                            string.Empty
                        }
                    });
                break;
        }
    }

    public static void WriteRevisions(IReadOnlyList<WordPressRevision> revisions, OutputFormat format, TextWriter writer)
    {
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(revisions, SerializerOptions));
                break;
            case OutputFormat.Raw:
                foreach (var revision in revisions)
                {
                    writer.WriteLine($"{revision.Id}\t{revision.Author}\t{GetTitleText(revision)}\t{revision.ModifiedGmt:u}");
                }
                break;
            default:
                WriteTable(writer,
                    new[] { "ID", "Author", "Modified (GMT)", "Title" },
                    revisions.Select(r => new[]
                    {
                        r.Id.ToString(),
                        r.Author.ToString(),
                        r.ModifiedGmt.ToString("u"),
                        GetTitleText(r)
                    }));
                break;
        }
    }

    public static void WriteRevision(WordPressRevision revision, OutputFormat format, TextWriter writer)
    {
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(revision, SerializerOptions));
                break;
            case OutputFormat.Raw:
                writer.WriteLine($"ID: {revision.Id}");
                writer.WriteLine($"Author: {revision.Author}");
                writer.WriteLine($"Title: {GetTitleText(revision)}");
                writer.WriteLine("--- CONTENT ---");
                writer.WriteLine(revision.Content?.Rendered ?? revision.Content?.Raw ?? string.Empty);
                break;
            default:
                WriteTable(writer,
                    new[] { "Field", "Value" },
                    new[]
                    {
                        new[] { "ID", revision.Id.ToString() },
                        new[] { "Author", revision.Author.ToString() },
                        new[] { "Modified (GMT)", revision.ModifiedGmt.ToString("u") },
                        new[] { "Title", GetTitleText(revision) },
                    });
                writer.WriteLine();
                writer.WriteLine("--- Content (Raw) ---");
                writer.WriteLine(revision.Content?.Raw ?? string.Empty);
                break;
        }
    }

    public static void WriteMediaItems(IReadOnlyList<WordPressMediaItem> mediaItems, OutputFormat format, TextWriter writer)
    {
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(mediaItems, SerializerOptions));
                break;
            case OutputFormat.Raw:
                foreach (var item in mediaItems)
                {
                    writer.WriteLine($"{item.Id}\t{item.MediaType}\t{GetTitleText(item)}\t{item.SourceUrl}");
                }
                break;
            default:
                WriteTable(writer,
                    new[] { "ID", "Type", "Title", "URL" },
                    mediaItems.Select(item => new[]
                    {
                        item.Id.ToString(),
                        item.MediaType ?? string.Empty,
                        Truncate(GetTitleText(item), 50),
                        item.SourceUrl ?? string.Empty
                    }));
                break;
        }
    }

    public static void WriteMediaItem(WordPressMediaItem item, OutputFormat format, TextWriter writer)
    {
        switch (format)
        {
            case OutputFormat.Json:
                writer.WriteLine(JsonSerializer.Serialize(item, SerializerOptions));
                break;
            case OutputFormat.Raw:
                writer.WriteLine($"{item.Id}\t{item.MediaType}\t{GetTitleText(item)}\t{item.SourceUrl}");
                break;
            default:
                WriteTable(writer,
                    new[] { "Field", "Value" },
                    new[]
                    {
                        new[] { "ID", item.Id.ToString() },
                        new[] { "Type", item.MediaType ?? string.Empty },
                        new[] { "Title", GetTitleText(item) },
                        new[] { "Mime Type", item.MimeType ?? string.Empty },
                        new[] { "Source URL", item.SourceUrl ?? string.Empty },
                    });
                break;
        }
    }

    private static void WriteTable(TextWriter writer, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var rowList = rows.Select(r => r.Select(c => c ?? string.Empty).ToArray()).ToList();
        var widths = new int[headers.Count];

        for (var i = 0; i < headers.Count; i++)
        {
            widths[i] = headers[i].Length;
        }

        foreach (var row in rowList)
        {
            for (var i = 0; i < widths.Length && i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        writer.WriteLine(BuildRow(headers, widths));
        writer.WriteLine(BuildSeparator(widths));

        foreach (var row in rowList)
        {
            writer.WriteLine(BuildRow(row, widths));
        }
    }

    private static string BuildRow(IReadOnlyList<string> cells, int[] widths)
    {
        var segments = new string[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            var value = cells[i] ?? string.Empty;
            segments[i] = value.PadRight(widths[i]);
        }

        return string.Join("  ", segments);
    }

    private static string BuildSeparator(int[] widths)
        => string.Join("  ", widths.Select(w => new string('-', w)));

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value.Substring(0, maxLength - 1) + "…";
    }

    private static string GetTitleText(IHasTitle post)
    {
        var title = post.Title?.Rendered ?? post.Title?.Raw;
        if (string.IsNullOrWhiteSpace(title))
        {
            if (!string.IsNullOrWhiteSpace(post.Slug))
            {
                return post.Slug;
            }
            return "(untitled)";
        }

        return title;
    }
}
