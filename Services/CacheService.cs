using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WpAiCli.Services.Data;
using WpAiCli.WordPress.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WpAiCli.Services;

public class CachePostMetadata
{
    public WordPressPostDetail Post { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string EditableMetaHash { get; set; } = null!;
}

public class EditablePostMetadata
{
    [YamlMember(Alias = "title")]
    public string? Title { get; set; }
    [YamlMember(Alias = "slug")]
    public string? Slug { get; set; }
    [YamlMember(Alias = "status")]
    public string? Status { get; set; }
    [YamlMember(Alias = "date")]
    public DateTime? Date { get; set; }
    [YamlMember(Alias = "excerpt")]
    public string? Excerpt { get; set; }
    [YamlMember(Alias = "featured_media")]
    public int? FeaturedMedia { get; set; }
    [YamlMember(Alias = "comment_status")]
    public string? CommentStatus { get; set; }
    [YamlMember(Alias = "ping_status")]
    public string? PingStatus { get; set; }
    [YamlMember(Alias = "categories")]
    public List<string>? Categories { get; set; }
    [YamlMember(Alias = "tags")]
    public List<string>? Tags { get; set; }
}

public class EditableCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class EditableTag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class CacheService
{
    private readonly CacheDbContext _db;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public CacheService(string cachePath)
    {
        var dbPath = Path.Combine(cachePath, "cache.db");
        _db = new CacheDbContext(dbPath);
    }

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
            PingStatus = post.PingStatus,
            Categories = post.Categories?.Select(c => c.ToString()).ToList(),
            Tags = post.Tags?.Select(t => t.ToString()).ToList()
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

        // 3. Save metadata to database
        var existingPost = _db.Posts.FirstOrDefault(p => p.PostId == post.Id);

        if (existingPost != null)
        {
            existingPost.Title = post.Title?.Raw ?? string.Empty;
            existingPost.Slug = post.Slug ?? string.Empty;
            existingPost.Status = post.Status ?? string.Empty;
            existingPost.Date = post.Date.GetValueOrDefault();
            existingPost.ContentHash = contentHash;
            existingPost.EditableMetaHash = editableMetaHash;
            existingPost.RawPostJson = JsonSerializer.Serialize(post, SerializerOptions);
            existingPost.LastModified = DateTime.UtcNow;
            _db.Posts.Update(existingPost);
        }
        else
        {
            var cachedPost = new CachedPost
            {
                PostId = post.Id,
                Title = post.Title?.Raw ?? string.Empty,
                Slug = post.Slug ?? string.Empty,
                Status = post.Status ?? string.Empty,
                Date = post.Date.GetValueOrDefault(),
                ContentHash = contentHash,
                EditableMetaHash = editableMetaHash,
                RawPostJson = JsonSerializer.Serialize(post, SerializerOptions),
                LastModified = DateTime.UtcNow
            };
            _db.Posts.Add(cachedPost);
        }

        _db.SaveChanges();
    }

    public async Task UpdateTaxonomiesCacheAsync(string cachePath, IEnumerable<WordPressCategory> categories, IEnumerable<WordPressTag> tags)
    {
        _db.ChangeTracker.Clear();
        // Update database
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Categories");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Tags");

        var newCategories = categories.Select(c => new CachedCategory { Id = c.Id, Name = c.Name ?? string.Empty, Slug = c.Slug ?? string.Empty });
        await _db.Categories.AddRangeAsync(newCategories);

        var newTags = tags.Select(t => new CachedTag { Id = t.Id, Name = t.Name ?? string.Empty, Slug = t.Slug ?? string.Empty });
        await _db.Tags.AddRangeAsync(newTags);

        await _db.SaveChangesAsync();

        // --- Start Refactoring: Individual YAML files ---

        // 1. Define and clean up directories
        var categoriesDir = Path.Combine(cachePath, "categories");
        var tagsDir = Path.Combine(cachePath, "tags");

        if (Directory.Exists(categoriesDir))
        {
            Directory.Delete(categoriesDir, recursive: true);
        }
        Directory.CreateDirectory(categoriesDir);

        if (Directory.Exists(tagsDir))
        {
            Directory.Delete(tagsDir, recursive: true);
        }
        Directory.CreateDirectory(tagsDir);

        // Delete old single files if they exist
        File.Delete(Path.Combine(cachePath, "categories.yaml"));
        File.Delete(Path.Combine(cachePath, "tags.yaml"));
        await _db.States.Where(s => s.Key == "categories_yaml_hash" || s.Key == "tags_yaml_hash").ExecuteDeleteAsync();


        // 2. Write individual category files
        foreach (var category in categories)
        {
            var editableCategory = new EditableCategory { Id = category.Id, Name = category.Name ?? string.Empty, Slug = category.Slug ?? string.Empty };
            var yamlContent = YamlSerializer.Serialize(editableCategory);
            var sanitizedName = SanitizeTitleForFilename(category.Name ?? string.Empty);
            var filePath = Path.Combine(categoriesDir, $"{category.Id}-{sanitizedName}.yaml");
            await File.WriteAllTextAsync(filePath, yamlContent);
            
            // Store hash for individual file
            var hash = ComputeSha256Hash(yamlContent);
            SetState($"category_{category.Id}_hash", hash);
        }

        // 3. Write individual tag files
        foreach (var tag in tags)
        {
            var editableTag = new EditableTag { Id = tag.Id, Name = tag.Name ?? string.Empty, Slug = tag.Slug ?? string.Empty };
            var yamlContent = YamlSerializer.Serialize(editableTag);
            var sanitizedName = SanitizeTitleForFilename(tag.Name ?? string.Empty);
            var filePath = Path.Combine(tagsDir, $"{tag.Id}-{sanitizedName}.yaml");
            await File.WriteAllTextAsync(filePath, yamlContent);

            // Store hash for individual file
            var hash = ComputeSha256Hash(yamlContent);
            SetState($"tag_{tag.Id}_hash", hash);
        }
        
        await _db.SaveChangesAsync();
        // --- End Refactoring ---
    }

    public List<CachePostMetadata> ListLocalPostMetadata(string cachePath)
    {
        var postsDir = Path.Combine(cachePath, "posts");
        if (!Directory.Exists(postsDir)) return new List<CachePostMetadata>();

        var cachedPosts = _db.Posts.ToList();
        var metadataList = new List<CachePostMetadata>();

        foreach (var cachedPost in cachedPosts)
        {
            try
            {
                var post = JsonSerializer.Deserialize<WordPressPostDetail>(cachedPost.RawPostJson, SerializerOptions);
                if (post != null)
                {
                    metadataList.Add(new CachePostMetadata
                    {
                        Post = post,
                        ContentHash = cachedPost.ContentHash,
                        EditableMetaHash = cachedPost.EditableMetaHash
                    });
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Skipping malformed post data in DB for ID {cachedPost.PostId}. Error: {ex.Message}");
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
            return DeserializeFromYaml<EditablePostMetadata>(File.ReadAllText(editableFile));
        }
        return null;
    }

    public (List<CachedCategory> Categories, List<CachedTag> Tags) GetTaxonomies()
    {
        return (_db.Categories.ToList(), _db.Tags.ToList());
    }

    public (List<EditableCategory> Categories, List<EditableTag> Tags) ReadLocalTaxonomies(string cachePath)
    {
        var categories = new List<EditableCategory>();
        var categoriesDir = Path.Combine(cachePath, "categories");
        if (Directory.Exists(categoriesDir))
        {
            foreach (var file in Directory.GetFiles(categoriesDir, "*.yaml"))
            {
                try
                {
                    var yamlContent = File.ReadAllText(file);
                    var category = DeserializeFromYaml<EditableCategory>(yamlContent);
                    categories.Add(category);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Skipping malformed category file: {Path.GetFileName(file)}. Error: {ex.Message}");
                }
            }
        }

        var tags = new List<EditableTag>();
        var tagsDir = Path.Combine(cachePath, "tags");
        if (Directory.Exists(tagsDir))
        {
            foreach (var file in Directory.GetFiles(tagsDir, "*.yaml"))
            {
                try
                {
                    var yamlContent = File.ReadAllText(file);
                    var tag = DeserializeFromYaml<EditableTag>(yamlContent);
                    tags.Add(tag);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Skipping malformed tag file: {Path.GetFileName(file)}. Error: {ex.Message}");
                }
            }
        }

        return (categories, tags);
    }

    public bool AreCacheFilesPresent(int postId, string cachePath)
    {
        var contentFile = FindFileByPattern(cachePath, $"{postId}-*_content.md");
        var editableFile = FindFileByPattern(cachePath, $"{postId}-*_editable.yaml");
        return File.Exists(contentFile) && File.Exists(editableFile);
    }

    public (bool contentFileExists, bool editableFileExists) CheckCacheFileExistence(int postId, string cachePath)
    {
        var contentFile = FindFileByPattern(cachePath, $"{postId}-*_content.md");
        var editableFile = FindFileByPattern(cachePath, $"{postId}-*_editable.yaml");
        return (File.Exists(contentFile), File.Exists(editableFile));
    }

    public void DeletePostFromCache(int postId, string cachePath)
    {
        // 1. Delete files from filesystem
        var postsDir = Path.Combine(cachePath, "posts");
        if (Directory.Exists(postsDir))
        {
            var filesToDelete = Directory.GetFiles(postsDir, $"{postId}-*");
            foreach (var file in filesToDelete)
            {
                File.Delete(file);
            }
        }

        // 2. Delete record from database
        var postInDb = _db.Posts.FirstOrDefault(p => p.PostId == postId);
        if (postInDb != null)
        {
            _db.Posts.Remove(postInDb);
            _db.SaveChanges();
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

    public int? FindCategoryId(string nameOrSlug)
    {
        var normalized = nameOrSlug.Trim();
        var allCategories = _db.Categories.ToList();

        // --- Start Debugging Output ---
        Console.Error.WriteLine($"--- DEBUG: FindCategoryId ---");
        Console.Error.WriteLine($"Searching for term: '{normalized}'");
        Console.Error.WriteLine($"Total categories in cache: {allCategories.Count}");
        foreach (var cat in allCategories)
        {
            bool isMatch = string.Equals(cat.Name, normalized, StringComparison.InvariantCultureIgnoreCase);
            Console.Error.WriteLine($"  - Comparing with: '{cat.Name}' (Slug: {cat.Slug}) | Match: {isMatch}");
        }
        Console.Error.WriteLine($"--- END DEBUG ---");
        // --- End Debugging Output ---

        var category = allCategories.FirstOrDefault(c => 
            string.Equals(c.Name, normalized, StringComparison.InvariantCultureIgnoreCase) || 
            string.Equals(c.Slug, normalized, StringComparison.InvariantCultureIgnoreCase));
        return category?.Id;
    }

    public int? FindTagId(string nameOrSlug)
    {
        var normalized = nameOrSlug.Trim();
        var tag = _db.Tags.ToList().FirstOrDefault(t => 
            string.Equals(t.Name, normalized, StringComparison.InvariantCultureIgnoreCase) || 
            string.Equals(t.Slug, normalized, StringComparison.InvariantCultureIgnoreCase));
        return tag?.Id;
    }

    public string? GetState(string key)
    {
        return _db.States.FirstOrDefault(s => s.Key == key)?.Value;
    }

    public void SetState(string key, string value)
    {
        var state = _db.States.FirstOrDefault(s => s.Key == key);
        if (state != null)
        {
            state.Value = value;
            _db.States.Update(state);
        }
        else
        {
            _db.States.Add(new CacheState { Key = key, Value = value });
        }
        _db.SaveChanges();
    }

    public string SerializeToYaml(EditablePostMetadata data)
    {
        return YamlSerializer.Serialize(data);
    }

    public T DeserializeFromYaml<T>(string yaml) where T : class, new()
    {
        return YamlDeserializer.Deserialize<T>(yaml) ?? new T();
    }
}
