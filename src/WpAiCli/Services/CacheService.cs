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
// using YamlDotNet.Serialization.TypeInspectors; // Removed to avoid duplicate registration issues

namespace WpAiCli.Services;

public class CachePostMetadata
{
    public WordPressPostDetail Post { get; set; } = null!;
    public string FileHash { get; set; } = null!;
}

public class LocalPost
{
    public EditablePostMetadata Metadata { get; set; } = new();
    public string Content { get; set; } = string.Empty;
}

public class EditablePostMetadata
{
    [YamlMember(Alias = "url")]
    public string? Url { get; set; }
    [YamlMember(Alias = "title")]
    public string? Title { get; set; }
    [YamlMember(Alias = "slug")]
    public string? Slug { get; set; }
    [YamlMember(Alias = "status")]
    public string? Status { get; set; }
    [YamlMember(Alias = "date")]
    public string? Date { get; set; }
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
    [YamlMember(Alias = "edit_mode")]
    public string? EditMode { get; set; }
    [YamlMember(Alias = "meta")]
    public Dictionary<string, object?>? Meta { get; set; }
}

public class EditableCategory
{
    [YamlMember(Alias = "url")]
    public string? Url { get; set; }
    [YamlMember(Alias = "id")]
    public int Id { get; set; }
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;
    [YamlMember(Alias = "slug")]
    public string Slug { get; set; } = string.Empty;
    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;
    [YamlMember(Alias = "parent_id")]
    public int? ParentId { get; set; }
    [YamlMember(Alias = "parent_slug")]
    public string? ParentSlug { get; set; }
    [YamlMember(Alias = "parent_ref")]
    public string? ParentRef { get; set; }
}

public class EditableTag
{
    [YamlMember(Alias = "url")]
    public string? Url { get; set; }
    [YamlMember(Alias = "id")]
    public int Id { get; set; }
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;
    [YamlMember(Alias = "slug")]
    public string Slug { get; set; } = string.Empty;
    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;
}

public class EditableMediaMetadata
{
    [YamlMember(Alias = "url")]
    public string? Url { get; set; }
    [YamlMember(Alias = "source_url")]
    public string? SourceUrl { get; set; }
    [YamlMember(Alias = "modified_gmt")]
    public DateTime? ModifiedGmt { get; set; }
    [YamlMember(Alias = "title")]
    public string? Title { get; set; }
    [YamlMember(Alias = "alt_text")]
    public string? AltText { get; set; }
    [YamlMember(Alias = "caption")]
    public string? Caption { get; set; }
    [YamlMember(Alias = "description")]
    public string? Description { get; set; }
}

public class CacheService
{
    private readonly CacheDbContext _db;
    private readonly string _cachePath;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve)
        .Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    // Cache for parsed content (Title, Content) to avoid re-reading files
    private readonly Dictionary<int, (string? Title, string Content)> _postContentCache = new();



    public CacheService(string rootCachePath, string connectionName)
    {
        _cachePath = Path.Combine(rootCachePath, connectionName);
        Directory.CreateDirectory(_cachePath);
        var dbPath = Path.Combine(_cachePath, "wp-ai-cache.db");
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

    private object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intValue)) return intValue;
                if (element.TryGetInt64(out var longValue)) return longValue;
                return element.GetDouble();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Null:
                return null;
            default:
                // For complex types (Array, Object), return the raw JSON text.
                // YamlDotNet can often handle this better than a half-deserialized object.
                return element.GetRawText();
        }
    }


    public void SavePostToCache(WordPressPostDetail post)
    {
        // Determine the correct directory based on post status
        var status = post.Status ?? "draft";
        var statusDir = Path.Combine(_cachePath, "posts", status);
        Directory.CreateDirectory(statusDir);

        var titleForFile = post.Title?.Raw ?? post.Slug;
        var sanitizedTitle = SanitizeTitleForFilename(titleForFile ?? string.Empty);
        var fileBaseName = $"{post.Id}-{sanitizedTitle}";
        var filePath = Path.Combine(statusDir, $"{fileBaseName}.md");

        // Clean up any old files for this post ID (now searches recursively)
        DeletePostFromCache(post.Id);

        // 1. Determine content and edit mode from meta field
        string contentToSave;
        bool hasMarkdownMeta = false;
        if (post.Meta != null && post.Meta.TryGetValue("_md_source", out var markdownSourceObj) && markdownSourceObj is JsonElement markdownJson && markdownJson.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(markdownJson.GetString()))
        {
            contentToSave = markdownJson.GetString()!;
            hasMarkdownMeta = true;
        }
        else
        {
            contentToSave = post.Content?.Raw ?? string.Empty;
        }

        // Get taxonomies for name mapping
        var (allCategories, allTags) = GetTaxonomies();
        var categoryMap = allCategories.ToDictionary(c => c.Id, c => c.Name);
        var tagMap = allTags.ToDictionary(t => t.Id, t => t.Name);

        // 2. Populate all metadata into the EditablePostMetadata object
        var editableMeta = new EditablePostMetadata
        {
            Url = post.Link,
            Title = post.Title?.Raw,
            EditMode = hasMarkdownMeta ? "markdown" : "html",
            Slug = System.Net.WebUtility.UrlDecode(post.Slug),
            Status = post.Status,
            Date = post.Date?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            Excerpt = post.Excerpt?.Raw,
            FeaturedMedia = post.FeaturedMedia,
            CommentStatus = post.CommentStatus,
            PingStatus = post.PingStatus,
            Categories = post.Categories?.Select(c => categoryMap.TryGetValue(c, out var name) ? $"{c}-{name}" : c.ToString()).ToList(),
            Tags = post.Tags?.Select(t => tagMap.TryGetValue(t, out var name) ? $"{t}-{name}" : t.ToString()).ToList(),
            Meta = post.Meta?.Where(kv => kv.Key != "_md_source").ToDictionary(kv => kv.Key, kv => ConvertJsonElement((JsonElement)kv.Value))
        };
        var yamlContent = SerializeToYaml(editableMeta);

        // 3. Construct the full file content
        var finalContent = string.Join("\n", "---", yamlContent, "---", "", contentToSave);
        File.WriteAllText(filePath, finalContent);
        
        // 4. Compute a single hash for the entire file
        var fileHash = ComputeSha256Hash(finalContent);

        // 5. Save metadata to database with the single hash
        var existingPost = _db.Posts.FirstOrDefault(p => p.PostId == post.Id);
        if (existingPost != null)
        {
            existingPost.Title = post.Title?.Raw ?? string.Empty;
            existingPost.Slug = post.Slug ?? string.Empty;
            existingPost.Status = post.Status ?? string.Empty;
            existingPost.Date = post.Date.GetValueOrDefault();
            existingPost.ServerLastModified = post.Modified.GetValueOrDefault();
            existingPost.FileHash = fileHash; // Use single hash
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
                ServerLastModified = post.Modified.GetValueOrDefault(),
                FileHash = fileHash, // Use single hash
                RawPostJson = JsonSerializer.Serialize(post, SerializerOptions),
                LastModified = DateTime.UtcNow
            };
            _db.Posts.Add(cachedPost);
        }

        _db.SaveChanges();
    }

    public void SaveRevisionToCache(WordPressRevision revision)
    {
        // 1. Define directory and file path
        var revisionDir = Path.Combine(_cachePath, "revisions", $"post_{revision.Parent}");
        Directory.CreateDirectory(revisionDir);
        var filePath = Path.Combine(revisionDir, $"{revision.Id}.md");

        // 2. Populate metadata from the revision object
        var editableMeta = new
        {
            Title = revision.Title?.Raw,
            revision.Slug,
            Date = revision.DateGmt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            EditMode = "html"
        };
        var yamlContent = YamlSerializer.Serialize(editableMeta);

        // 3. Get content (currently raw HTML, TODO: Convert to Markdown)
        var contentToSave = revision.Content?.Raw ?? string.Empty;

        // 4. Construct the full file content and write to disk
        var finalContent = string.Join("\n", "---", yamlContent, "---", "", contentToSave);
        File.WriteAllText(filePath, finalContent);
    }

    public LocalPost? ReadLocalPost(int postId)
    {
        var postFile = FindFileByPattern($"{postId}-*.md");
        if (string.IsNullOrEmpty(postFile) || !File.Exists(postFile))
        {
            return null;
        }

        var fileContent = File.ReadAllText(postFile);
        var parts = fileContent.Split(new[] { "---" }, 3, StringSplitOptions.None);

        if (parts.Length < 3)
        {
            // Not a valid front matter format, treat all as content
            return new LocalPost { Content = fileContent };
        }

        var yaml = parts[1];
        var content = parts[2].TrimStart();
        
        var metadata = DeserializeFromYaml<EditablePostMetadata>(yaml);
        ValidatePostMetadata(metadata, postFile);

        return new LocalPost
        {
            Metadata = metadata,
            Content = content
        };
    }

    private void ValidatePostMetadata(EditablePostMetadata metadata, string filePath)
    {
        if (metadata.Date != null && !DateTime.TryParse(metadata.Date, out _))
        {
            throw new InvalidOperationException($"Validation failed for {Path.GetFileName(filePath)}: Invalid format for 'date'. Please use 'yyyy-MM-dd HH:mm:ss'.");
        }

        var validStatuses = new HashSet<string> { "publish", "future", "draft", "pending", "private" };
        if (metadata.Status != null && !validStatuses.Contains(metadata.Status))
        {
            throw new InvalidOperationException($"Validation failed for {Path.GetFileName(filePath)}: Invalid value '{metadata.Status}' for 'status'. Valid values are: {string.Join(", ", validStatuses)}.");
        }

        var validCommentStatuses = new HashSet<string> { "open", "closed" };
        if (metadata.CommentStatus != null && !validCommentStatuses.Contains(metadata.CommentStatus))
        {
            throw new InvalidOperationException($"Validation failed for {Path.GetFileName(filePath)}: Invalid value '{metadata.CommentStatus}' for 'commentStatus'. Valid values are: {string.Join(", ", validCommentStatuses)}.");
        }

        if (metadata.PingStatus != null && !validCommentStatuses.Contains(metadata.PingStatus)) // Same values as commentStatus
        {
            throw new InvalidOperationException($"Validation failed for {Path.GetFileName(filePath)}: Invalid value '{metadata.PingStatus}' for 'pingStatus'. Valid values are: {string.Join(", ", validCommentStatuses)}.");
        }

        var validEditModes = new HashSet<string> { "markdown", "html" };
        if (metadata.EditMode != null && !validEditModes.Contains(metadata.EditMode))
        {
            throw new InvalidOperationException($"Validation failed for {Path.GetFileName(filePath)}: Invalid value '{metadata.EditMode}' for 'editMode'. Valid values are: {string.Join(", ", validEditModes)}.");
        }
    }

    public async Task UpdateTaxonomiesCacheAsync(IEnumerable<WordPressCategory> categories, IEnumerable<WordPressTag> tags)
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
        var categoriesDir = Path.Combine(_cachePath, "categories");
        var tagsDir = Path.Combine(_cachePath, "tags");

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
        File.Delete(Path.Combine(_cachePath, "categories.yaml"));
        File.Delete(Path.Combine(_cachePath, "tags.yaml"));
        await _db.States.Where(s => s.Key == "categories_yaml_hash" || s.Key == "tags_yaml_hash").ExecuteDeleteAsync();


        // 2. Write individual category files
        var categoryList = categories.ToList();
        var categoriesById = categoryList.ToDictionary(c => c.Id, c => c);
        foreach (var category in categoryList)
        {
            int? parentId = (category.Parent.HasValue && category.Parent.Value > 0) ? category.Parent.Value : (int?)null;
            string? parentSlug = null;
            string? parentRef = null;
            if (parentId.HasValue && categoriesById.TryGetValue(parentId.Value, out var parentCat))
            {
                parentSlug = parentCat.Slug ?? string.Empty;
                var parentName = parentCat.Name ?? string.Empty;
                parentRef = $"{parentId.Value}-{parentName}";
            }

            var editableCategory = new EditableCategory { Url = category.Link, Id = category.Id, Name = category.Name ?? string.Empty, Slug = System.Net.WebUtility.UrlDecode(category.Slug ?? string.Empty), Description = category.Description ?? string.Empty, ParentId = parentId, ParentSlug = parentSlug, ParentRef = parentRef };
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
            var editableTag = new EditableTag { Url = tag.Link, Id = tag.Id, Name = tag.Name ?? string.Empty, Slug = System.Net.WebUtility.UrlDecode(tag.Slug ?? string.Empty), Description = tag.Description ?? string.Empty };
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

    public void OrganizePostFiles()
    {
        var postsDir = Path.Combine(_cachePath, "posts");
        if (!Directory.Exists(postsDir)) return;

        var allPostFiles = Directory.GetFiles(postsDir, "*.md", SearchOption.AllDirectories);

        foreach (var filePath in allPostFiles)
        {
            try
            {
                var fileContent = File.ReadAllText(filePath);
                var parts = fileContent.Split(new[] { "---" }, 3, StringSplitOptions.None);

                if (parts.Length < 3) continue; // Not a valid front matter format

                var yaml = parts[1];
                var metadata = DeserializeFromYaml<EditablePostMetadata>(yaml);

                if (string.IsNullOrEmpty(metadata.Status)) continue;

                var currentStatus = Path.GetFileName(Path.GetDirectoryName(filePath));
                var targetStatus = metadata.Status;

                // Logic for future posts that have become published
                if (targetStatus == "future")
                {
                    if (DateTime.TryParse(metadata.Date, out var postDate) && postDate.ToUniversalTime() <= DateTime.UtcNow)
                    {
                        targetStatus = "publish";
                    }
                }

                if (currentStatus != targetStatus)
                {
                    var targetDir = Path.Combine(postsDir, targetStatus);
                    Directory.CreateDirectory(targetDir);
                    var newFilePath = Path.Combine(targetDir, Path.GetFileName(filePath));
                    File.Move(filePath, newFilePath);
                    Console.WriteLine($"Organized: Moved '{Path.GetFileName(filePath)}' from '{currentStatus}' to '{targetStatus}' folder.");

                    // BUG FIX: Update the database hash after moving the file
                    var fileName = Path.GetFileNameWithoutExtension(newFilePath);
                    if (int.TryParse(fileName.Split('-').FirstOrDefault(), out var postId))
                    {
                        var postInDb = _db.Posts.FirstOrDefault(p => p.PostId == postId);
                        if (postInDb != null)
                        {
                            // FIX: Use the canonical method to calculate the hash, matching the SyncService logic.
                            var canonicalContent = string.Join("\n", "---", YamlSerializer.Serialize(metadata), "---", "", parts[2].TrimStart());
                            var newHash = ComputeSha256Hash(canonicalContent);
                            postInDb.FileHash = newHash;
                            _db.SaveChanges();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error organizing file '{Path.GetFileName(filePath)}': {ex.Message}");
            }
        }
    }

    public List<CachePostMetadata> ListLocalPostMetadata()
    {
        var postsDir = Path.Combine(_cachePath, "posts");
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
                        FileHash = cachedPost.FileHash
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


    public (List<CachedCategory> Categories, List<CachedTag> Tags) GetTaxonomies()
    {
        return (_db.Categories.ToList(), _db.Tags.ToList());
    }

    public (List<(EditableCategory Category, string RawContent)> Categories, List<(EditableTag Tag, string RawContent)> Tags) ReadLocalTaxonomies()
    {
        var categories = new List<(EditableCategory, string)>();
        var categoriesDir = Path.Combine(_cachePath, "categories");
        if (Directory.Exists(categoriesDir))
        {
            foreach (var file in Directory.GetFiles(categoriesDir, "*.yaml"))
            {
                try
                {
                    var yamlContent = File.ReadAllText(file);
                    var category = DeserializeFromYaml<EditableCategory>(yamlContent);
                    categories.Add((category, yamlContent));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Skipping malformed category file: {Path.GetFileName(file)}. Error: {ex.Message}");
                }
            }
        }

        var tags = new List<(EditableTag, string)>();
        var tagsDir = Path.Combine(_cachePath, "tags");
        if (Directory.Exists(tagsDir))
        {
            foreach (var file in Directory.GetFiles(tagsDir, "*.yaml"))
            {
                try
                {
                    var yamlContent = File.ReadAllText(file);
                    var tag = DeserializeFromYaml<EditableTag>(yamlContent);
                    tags.Add((tag, yamlContent));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Skipping malformed tag file: {Path.GetFileName(file)}. Error: {ex.Message}");
                }
            }
        }

        return (categories, tags);
    }

    public string? GetMediaMetadataHash(int mediaId)
    {
        return _db.Media.FirstOrDefault(m => m.MediaId == mediaId)?.MetadataHash;
    }

    public List<(int MediaId, EditableMediaMetadata Metadata)> ReadLocalMediaMetadata()
    {
        var mediaMetadataList = new List<(int MediaId, EditableMediaMetadata Metadata)>();
        var mediaDir = Path.Combine(_cachePath, "media");

        if (!Directory.Exists(mediaDir)) return mediaMetadataList;

        foreach (var file in Directory.GetFiles(mediaDir, "*.yaml"))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var idString = fileName.Split('-').FirstOrDefault();
                if (int.TryParse(idString, out var mediaId))
                {
                    var yamlContent = File.ReadAllText(file);
                    var metadata = DeserializeFromYaml<EditableMediaMetadata>(yamlContent);
                    mediaMetadataList.Add((mediaId, metadata));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Skipping malformed media metadata file: {Path.GetFileName(file)}. Error: {ex.Message}");
            }
        }

        return mediaMetadataList;
    }

    public bool IsPostCacheFilePresent(int postId)
    {
        var postFile = FindFileByPattern($"{postId}-*.md");
        return File.Exists(postFile);
    }

    public void DeletePostFromCache(int postId)
    {
        // 1. Delete files from filesystem
        var postsDir = Path.Combine(_cachePath, "posts");
        if (Directory.Exists(postsDir))
        {
            var filesToDelete = Directory.GetFiles(postsDir, $"{postId}-*", SearchOption.AllDirectories);
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

    public void SaveCategoryToCache(WordPressCategory category)
    {
        var categoriesDir = Path.Combine(_cachePath, "categories");
        Directory.CreateDirectory(categoriesDir);

        // Clean up old files for this category ID first, to handle renames
        var oldFiles = Directory.GetFiles(categoriesDir, $"{category.Id}-*.yaml");
        foreach(var oldFile in oldFiles) File.Delete(oldFile);

        // Save to DB
        var cachedCategory = _db.Categories.FirstOrDefault(c => c.Id == category.Id);
        if (cachedCategory == null)
        {
            cachedCategory = new CachedCategory { Id = category.Id };
            _db.Categories.Add(cachedCategory);
        }
        cachedCategory.Name = category.Name ?? string.Empty;
        cachedCategory.Slug = category.Slug ?? string.Empty;
        _db.SaveChanges();

        // Save YAML file
        int? parentId = (category.Parent.HasValue && category.Parent.Value > 0) ? category.Parent.Value : (int?)null;
        string? parentSlug = null;
        string? parentRef = null;
        if (parentId.HasValue)
        {
            var parentCached = _db.Categories.FirstOrDefault(c => c.Id == parentId.Value);
            parentSlug = parentCached?.Slug;
            if (parentCached != null)
            {
                parentRef = $"{parentCached.Id}-{parentCached.Name}";
            }
        }
        var editableCategory = new EditableCategory { Url = category.Link, Id = category.Id, Name = category.Name ?? string.Empty, Slug = category.Slug ?? string.Empty, Description = category.Description ?? string.Empty, ParentId = parentId, ParentSlug = parentSlug, ParentRef = parentRef };
        var yamlContent = YamlSerializer.Serialize(editableCategory);
        var sanitizedName = SanitizeTitleForFilename(category.Name ?? string.Empty);
        var filePath = Path.Combine(categoriesDir, $"{category.Id}-{sanitizedName}.yaml");
        File.WriteAllText(filePath, yamlContent);
        
        // Store hash for individual file
        var hash = ComputeSha256Hash(yamlContent);
        SetState($"category_{category.Id}_hash", hash);
    }

    public void DeleteCategoryFromCache(int categoryId)
    {
        // Delete file
        var categoriesDir = Path.Combine(_cachePath, "categories");
        if (Directory.Exists(categoriesDir))
        {
            var filesToDelete = Directory.GetFiles(categoriesDir, $"{categoryId}-*.yaml");
            foreach (var file in filesToDelete)
            {
                File.Delete(file);
            }
        }

        // Delete from DB
        var categoryInDb = _db.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (categoryInDb != null)
        {
            _db.Categories.Remove(categoryInDb);
        }
        
        // Delete hash state
        var state = _db.States.FirstOrDefault(s => s.Key == $"category_{categoryId}_hash");
        if (state != null)
        {
            _db.States.Remove(state);
        }
        _db.SaveChanges();
    }

    public void SaveTagToCache(WordPressTag tag)
    {
        var tagsDir = Path.Combine(_cachePath, "tags");
        Directory.CreateDirectory(tagsDir);

        var oldFiles = Directory.GetFiles(tagsDir, $"{tag.Id}-*.yaml");
        foreach(var oldFile in oldFiles) File.Delete(oldFile);

        var cachedTag = _db.Tags.FirstOrDefault(t => t.Id == tag.Id);
        if (cachedTag == null)
        {
            cachedTag = new CachedTag { Id = tag.Id };
            _db.Tags.Add(cachedTag);
        }
        cachedTag.Name = tag.Name ?? string.Empty;
        cachedTag.Slug = tag.Slug ?? string.Empty;
        _db.SaveChanges();

        var editableTag = new EditableTag { Url = tag.Link, Id = tag.Id, Name = tag.Name ?? string.Empty, Slug = tag.Slug ?? string.Empty, Description = tag.Description ?? string.Empty };
        var yamlContent = YamlSerializer.Serialize(editableTag);
        var sanitizedName = SanitizeTitleForFilename(tag.Name ?? string.Empty);
        var filePath = Path.Combine(tagsDir, $"{tag.Id}-{sanitizedName}.yaml");
        File.WriteAllText(filePath, yamlContent);

        var hash = ComputeSha256Hash(yamlContent);
        SetState($"tag_{tag.Id}_hash", hash);
    }

    public void DeleteTagFromCache(int tagId)
    {
        var tagsDir = Path.Combine(_cachePath, "tags");
        if (Directory.Exists(tagsDir))
        {
            var filesToDelete = Directory.GetFiles(tagsDir, $"{tagId}-*.yaml");
            foreach (var file in filesToDelete)
            {
                File.Delete(file);
            }
        }

        var tagInDb = _db.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tagInDb != null)
        {
            _db.Tags.Remove(tagInDb);
        }
        
        var state = _db.States.FirstOrDefault(s => s.Key == $"tag_{tagId}_hash");
        if (state != null)
        {
            _db.States.Remove(state);
        }
        _db.SaveChanges();
    }

    public void CleanRevisionsCache(int? postId)
    {
        string pathToDelete;
        if (postId.HasValue)
        {
            pathToDelete = Path.Combine(_cachePath, "revisions", $"post_{postId.Value}");
        }
        else
        {
            pathToDelete = Path.Combine(_cachePath, "revisions");
        }

        if (Directory.Exists(pathToDelete))
        {
            Directory.Delete(pathToDelete, true);
            Console.WriteLine($"Successfully deleted: {pathToDelete}");
        }
        else
        {
            Console.WriteLine("No revision cache found to delete.");
        }
    }

    public void DeleteMediaFromCache(int mediaId)
    {
        // 1. Delete files from filesystem
        var mediaDir = Path.Combine(_cachePath, "media");
        if (Directory.Exists(mediaDir))
        {
            // Find files like {mediaId}-*.* and {mediaId}-*.yaml
            var filesToDelete = Directory.GetFiles(mediaDir, $"{mediaId}-*");
            foreach (var file in filesToDelete)
            {
                File.Delete(file);
            }
        }

        // 2. Delete record from database
        var mediaInDb = _db.Media.FirstOrDefault(m => m.MediaId == mediaId);
        if (mediaInDb != null)
        {
            _db.Media.Remove(mediaInDb);
            _db.SaveChanges();
        }
    }

    public bool IsMediaCached(int mediaId)
    {
        var mediaDir = Path.Combine(_cachePath, "media");
        if (!Directory.Exists(mediaDir)) return false;

        var files = Directory.GetFiles(mediaDir, $"{mediaId}-*.yaml");
        return files.Length > 0;
    }

    public void SaveMediaToCache(WordPressMedia media, byte[] fileContent)
    {
        var mediaDir = Path.Combine(_cachePath, "media");
        Directory.CreateDirectory(mediaDir);

        if (string.IsNullOrEmpty(media.SourceUrl))
        {
            throw new ArgumentException("Media SourceUrl cannot be null or empty.", nameof(media));
        }
        var fileName = Path.GetFileName(new Uri(media.SourceUrl).LocalPath);
        var fileBaseName = $"{media.Id}-{fileName}";
        var yamlFileName = $"{media.Id}-{Path.GetFileNameWithoutExtension(fileName)}.yaml";

        var editableMeta = new EditableMediaMetadata
        {
            Url = media.Link,
            SourceUrl = media.SourceUrl,
            ModifiedGmt = media.ModifiedGmt,
            Title = media.Title?.Raw,
            AltText = media.AltText,
            Caption = media.Caption?.Raw,
            Description = media.Description?.Raw
        };
        var yamlContent = SerializeToYaml(editableMeta);
        var editableMetaFilePath = Path.Combine(mediaDir, yamlFileName);
        File.WriteAllText(editableMetaFilePath, yamlContent);
        var editableMetaHash = ComputeSha256Hash(yamlContent);

        // 2. Handle binary file
        var mediaFilePath = Path.Combine(mediaDir, fileBaseName);
        File.WriteAllBytes(mediaFilePath, fileContent);
        var fileHash = ComputeSha256Hash(fileContent);

        // 3. Save metadata to database
        var cachedMedia = new CachedMedia
        {
            MediaId = media.Id,
            FileName = fileBaseName,
            FileHash = fileHash,
            MetadataHash = editableMetaHash,
            ServerLastModified = media.ModifiedGmt.GetValueOrDefault(),
            RawMediaJson = JsonSerializer.Serialize(media, SerializerOptions),
            LastModified = DateTime.UtcNow
        };
        _db.Media.Add(cachedMedia);
        _db.SaveChanges();
    }

    // Update only the media metadata YAML and DB hash without touching the binary file
    public void UpdateMediaMetadataOnly(WordPressMedia media)
    {
        var mediaDir = Path.Combine(_cachePath, "media");
        Directory.CreateDirectory(mediaDir);

        // Prepare editable metadata
        var editableMeta = new EditableMediaMetadata
        {
            Url = media.Link,
            SourceUrl = media.SourceUrl,
            ModifiedGmt = media.ModifiedGmt,
            Title = media.Title?.Raw,
            AltText = media.AltText,
            Caption = media.Caption?.Raw,
            Description = media.Description?.Raw
        };
        var yamlContent = SerializeToYaml(editableMeta);

        // Find existing YAML file for this media ID or create a new name
        var existingYaml = Directory.Exists(mediaDir) ? Directory.GetFiles(mediaDir, $"{media.Id}-*.yaml").FirstOrDefault() : null;
        string yamlPath;
        if (!string.IsNullOrEmpty(existingYaml))
        {
            yamlPath = existingYaml!;
        }
        else
        {
            // Derive from SourceUrl if available; otherwise fallback to generic name
            var baseName = !string.IsNullOrEmpty(media.SourceUrl)
                ? Path.GetFileNameWithoutExtension(new Uri(media.SourceUrl).LocalPath)
                : $"media-{media.Id}";
            yamlPath = Path.Combine(mediaDir, $"{media.Id}-{baseName}.yaml");
        }

        File.WriteAllText(yamlPath, yamlContent);
        var editableMetaHash = ComputeSha256Hash(yamlContent);

        // Update DB row if present
        var mediaInDb = _db.Media.FirstOrDefault(m => m.MediaId == media.Id);
        if (mediaInDb != null)
        {
            mediaInDb.MetadataHash = editableMetaHash;
            mediaInDb.ServerLastModified = media.ModifiedGmt.GetValueOrDefault();
            mediaInDb.RawMediaJson = JsonSerializer.Serialize(media, SerializerOptions);
            mediaInDb.LastModified = DateTime.UtcNow;
            _db.Media.Update(mediaInDb);
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

    public string ComputeSha256Hash(byte[] rawData)
    {
        using (SHA256 sha256Hash = SHA256.Create())
        {
            byte[] bytes = sha256Hash.ComputeHash(rawData);
            var builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
    
    public string? FindFileByPattern(string pattern)
    {
        var postsDir = Path.Combine(_cachePath, "posts");
        return Directory.Exists(postsDir) ? Directory.GetFiles(postsDir, pattern, SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    private string? FindMediaYamlFile(int mediaId)
    {
        var mediaDir = Path.Combine(_cachePath, "media");
        return Directory.Exists(mediaDir) ? Directory.GetFiles(mediaDir, $"{mediaId}-*.yaml", SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    public bool IsLocalMediaChanged(int mediaId)
    {
        var yamlFile = FindMediaYamlFile(mediaId);
        if (string.IsNullOrEmpty(yamlFile) || !File.Exists(yamlFile))
        {
            return false; // If there is no file, it cannot have changed.
        }

        var yamlContent = File.ReadAllText(yamlFile);
        var currentHash = ComputeSha256Hash(yamlContent);
        var previousHash = GetMediaMetadataHash(mediaId);

        // The change is detected if the previous hash exists and is different from the current one.
        return previousHash != null && currentHash != previousHash;
    }

    public int? FindCategoryId(string nameOrSlug)
    {
        var normalized = nameOrSlug.Trim();
        var allCategories = _db.Categories.ToList();

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

    public string SerializeToYaml(EditableMediaMetadata data)
    {
        return YamlSerializer.Serialize(data);
    }

    public T DeserializeFromYaml<T>(string yaml) where T : class, new()
    {
        return YamlDeserializer.Deserialize<T>(yaml) ?? new T();
    }

    public async Task<T?> GetLocalTaxonomyTermAsync<T>(string type, int id) where T : class, new()
    {
        var dir = Path.Combine(_cachePath, type == "category" ? "categories" : "tags");
        if (!Directory.Exists(dir)) return null;

        var file = Directory.GetFiles(dir, $"{id}-*.yaml").FirstOrDefault();
        if (file == null)
        {
            return null;
        }

        var yamlContent = await File.ReadAllTextAsync(file);
        return DeserializeFromYaml<T>(yamlContent);
    }

    public async Task UpdateLocalTaxonomyTermAsync(WordPressTerm term, bool updateHashOnly = false)
    {
        var type = term is WordPressCategory ? "category" : "tag";
        var dir = Path.Combine(_cachePath, type == "category" ? "categories" : "tags");
        Directory.CreateDirectory(dir);

        string yamlContent;
        if (!updateHashOnly)
        {
            // Find and delete the old file for this ID, as the name might have changed
            var oldFiles = Directory.GetFiles(dir, $"{term.Id}-*.yaml");
            foreach (var oldFile in oldFiles) File.Delete(oldFile);

            object editableTerm;
            if (term is WordPressCategory cat)
            {
                int? parentId = (cat.Parent.HasValue && cat.Parent.Value > 0) ? cat.Parent.Value : (int?)null;
                string? parentSlug = null;
                string? parentRef = null;
                if (parentId.HasValue)
                {
                    var parentCached = _db.Categories.FirstOrDefault(c => c.Id == parentId.Value);
                    parentSlug = parentCached?.Slug;
                    if (parentCached != null)
                    {
                        parentRef = $"{parentCached.Id}-{parentCached.Name}";
                    }
                }
                editableTerm = new EditableCategory { Url = cat.Link, Id = cat.Id, Name = cat.Name ?? string.Empty, Slug = cat.Slug ?? string.Empty, Description = cat.Description ?? string.Empty, ParentId = parentId, ParentSlug = parentSlug, ParentRef = parentRef };
            }
            else if (term is WordPressTag tag)
            {
                editableTerm = new EditableTag { Url = tag.Link, Id = tag.Id, Name = tag.Name ?? string.Empty, Slug = tag.Slug ?? string.Empty, Description = tag.Description ?? string.Empty };
            }
            else
            {
                return;
            }
            yamlContent = YamlSerializer.Serialize(editableTerm);
            var sanitizedName = SanitizeTitleForFilename(term.Name ?? string.Empty);
            var filePath = Path.Combine(dir, $"{term.Id}-{sanitizedName}.yaml");
            await File.WriteAllTextAsync(filePath, yamlContent);
        }
        else
        {
            // Hash only: read the content of the file that must already exist
            var file = Directory.GetFiles(dir, $"{term.Id}-*.yaml").FirstOrDefault();
            if (file == null) throw new FileNotFoundException($"Cannot update hash for non-existent {type} {term.Id}");
            yamlContent = await File.ReadAllTextAsync(file);
        }

        var hash = ComputeSha256Hash(yamlContent);
        SetState($"{type}_{term.Id}_hash", hash);
        await _db.SaveChangesAsync();
    }
}
