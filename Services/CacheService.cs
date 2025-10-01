using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WpAiCli.WordPress.Models;

namespace WpAiCli.Services;

public class CachePostMetadata
{
    public WordPressPostDetail Post { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string EditableMetaHash { get; set; } = null!;
}

public class EditablePostMetadata
{
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public string? Status { get; set; }
    public DateTime? Date { get; set; }
    public string? Excerpt { get; set; }
    public int? FeaturedMedia { get; set; }
    public string? CommentStatus { get; set; }
    public string? PingStatus { get; set; }
}

public class CacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string SanitizeTitleForFilename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "untitled";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedTitle = new string(title.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());

        sanitizedTitle = Regex.Replace(sanitizedTitle.Trim(), "-{2,}", "-");

        const int maxLen = 100;
        if (sanitizedTitle.Length > maxLen)
        {
            sanitizedTitle = sanitizedTitle.Substring(0, maxLen);
        }

        return sanitizedTitle;
    }

    public void SavePostToCache(WordPressPostDetail post, string cachePath)
    {
        var postsDir = Path.Combine(cachePath, "posts");
        Directory.CreateDirectory(postsDir);

        var sanitizedTitle = SanitizeTitleForFilename(post.Title?.Raw ?? post.Slug ?? string.Empty);
        var fileBaseName = $"{post.Id}-{sanitizedTitle}";

        DeletePostFromCache(post.Id, cachePath);

        // 1. Handle editable.yaml
        var editableMeta = new EditablePostMetadata
        {
            Title = post.Title?.Raw,
            Slug = post.Slug,
            Status = post.Status,
            Date = post.Date,
            Excerpt = post.Excerpt?.Raw,
            FeaturedMedia = post.FeaturedMedia,
            CommentStatus = post.CommentStatus,
            PingStatus = post.PingStatus
        };
        var yamlContent = SerializeToYaml(editableMeta);
        var editableMetaFilePath = Path.Combine(postsDir, $"{fileBaseName}_editable.yaml");
        File.WriteAllText(editableMetaFilePath, yamlContent);
        var editableMetaHash = ComputeSha256Hash(yamlContent);

        // 2. Handle content.md
        var contentFilePath = Path.Combine(postsDir, $"{fileBaseName}_content.md");
        var content = post.Content?.Raw ?? string.Empty;
        File.WriteAllText(contentFilePath, content);
        var contentHash = ComputeSha256Hash(content);

        // 3. Handle meta.json (source of truth + hashes)
        var metadata = new CachePostMetadata
        {
            Post = post,
            ContentHash = contentHash,
            EditableMetaHash = editableMetaHash
        };
        var metaFilePath = Path.Combine(postsDir, $"{fileBaseName}_meta.json");
        var metaJson = JsonSerializer.Serialize(metadata, SerializerOptions);
        File.WriteAllText(metaFilePath, metaJson);
    }

    public List<CachePostMetadata> ListLocalPostMetadata(string cachePath)
    {
        var postsDir = Path.Combine(cachePath, "posts");
        if (!Directory.Exists(postsDir))
        {
            return new List<CachePostMetadata>();
        }

        var metadataList = new List<CachePostMetadata>();
        var metaFiles = Directory.GetFiles(postsDir, "*_meta.json");

        foreach (var metaFile in metaFiles)
        {
            try
            {
                var json = File.ReadAllText(metaFile);
                var metadata = JsonSerializer.Deserialize<CachePostMetadata>(json, SerializerOptions);
                if (metadata != null)
                {
                    metadataList.Add(metadata);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Skipping malformed metadata file: {Path.GetFileName(metaFile)}. Error: {ex.Message}");
            }
        }
        return metadataList;
    }

    public string ReadLocalContent(int postId, string cachePath)
    {
        var contentFile = FindFileByPattern(cachePath, $"{postId}-*_content.md");
        if (File.Exists(contentFile))
        {
            return File.ReadAllText(contentFile);
        }
        return string.Empty;
    }
    
    public EditablePostMetadata? ReadEditableMetadata(int postId, string cachePath)
    {
        var editableFile = FindFileByPattern(cachePath, $"{postId}-*_editable.yaml");
        if (File.Exists(editableFile))
        {
            return DeserializeFromYaml(File.ReadAllText(editableFile));
        }
        return null;
    }

    public void DeletePostFromCache(int postId, string cachePath)
    {
        var postsDir = Path.Combine(cachePath, "posts");
        if (!Directory.Exists(postsDir)) return;

        var filesToDelete = Directory.GetFiles(postsDir, $"{postId}-*");

        foreach (var file in filesToDelete)
        {
            File.Delete(file);
        }
    }

    public string ComputeSha256Hash(string rawData)
    {
        using (SHA256 sha256Hash = SHA256.Create())
        {
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            var builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
    
    public string? FindFileByPattern(string cachePath, string pattern)
    {
        var postsDir = Path.Combine(cachePath, "posts");
        return Directory.Exists(postsDir) ? Directory.GetFiles(postsDir, pattern).FirstOrDefault() : null;
    }

    public string SerializeToYaml(EditablePostMetadata data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"title: '{(data.Title?.Replace("'", "''"))}'");
        sb.AppendLine($"slug: '{(data.Slug?.Replace("'", "''"))}'");
        sb.AppendLine($"status: '{(data.Status?.Replace("'", "''"))}'");
        sb.AppendLine($"date: '{(data.Date?.ToString("o"))}'"); // ISO 8601 format
        sb.AppendLine($"excerpt: '{(data.Excerpt?.Replace("'", "''"))}'");
        sb.AppendLine($"featured_media: {data.FeaturedMedia}");
        sb.AppendLine($"comment_status: '{(data.CommentStatus?.Replace("'", "''"))}'");
        sb.AppendLine($"ping_status: '{(data.PingStatus?.Replace("'", "''"))}'");
        return sb.ToString();
    }

    public EditablePostMetadata DeserializeFromYaml(string yaml)
    {
        var data = new EditablePostMetadata();
        var lines = yaml.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split(new[] { ':' }, 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            if (value.StartsWith("'") && value.EndsWith("'"))
            {
                value = value.Substring(1, value.Length - 2);
            }

            switch (key)
            {
                case "title":
                    data.Title = value.Replace("''", "'");
                    break;
                case "slug":
                    data.Slug = value.Replace("''", "'");
                    break;
                case "status":
                    data.Status = value.Replace("''", "'");
                    break;
                case "date":
                    if (DateTime.TryParse(value, out var dateValue))
                        data.Date = dateValue;
                    break;
                case "excerpt":
                    data.Excerpt = value.Replace("''", "'");
                    break;
                case "featured_media":
                    if (int.TryParse(value, out var intValue))
                        data.FeaturedMedia = intValue;
                    break;
                case "comment_status":
                    data.CommentStatus = value.Replace("''", "'");
                    break;
                case "ping_status":
                    data.PingStatus = value.Replace("''", "'");
                    break;
            }
        }
        return data;
    }
}
