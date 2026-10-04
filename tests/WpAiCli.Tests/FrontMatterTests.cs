using WpAiCli.Services;
using Xunit;

namespace WpAiCli.Tests;

public class FrontMatterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CacheService _cacheService;

    public FrontMatterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wpai_frontmatter_tests_" + Guid.NewGuid().ToString("N"));
        _cacheService = new CacheService(_tempDir, "test-conn");
    }

    public void Dispose()
    {
        _cacheService.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void SerializeAndDeserialize_EditablePostMetadata_PreservesAllFields()
    {
        var original = new EditablePostMetadata
        {
            Title = "Test Title",
            Slug = "test-title",
            Status = "publish",
            Date = "2026-10-04 12:00:00",
            Excerpt = "This is a brief summary.",
            CommentStatus = "open",
            PingStatus = "closed",
            EditMode = "markdown",
            Categories = new List<string> { "News", "Tech" },
            Tags = new List<string> { "csharp", "dotnet" },
            Meta = new Dictionary<string, object?>
            {
                ["_custom_field"] = "custom_value",
                ["_views_count"] = 42
            }
        };

        var yaml = _cacheService.SerializeToYaml(original);
        Assert.NotNull(yaml);

        var deserialized = _cacheService.DeserializeFromYaml<EditablePostMetadata>(yaml);

        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Slug, deserialized.Slug);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.Date, deserialized.Date);
        Assert.Equal(original.Excerpt, deserialized.Excerpt);
        Assert.Equal(original.CommentStatus, deserialized.CommentStatus);
        Assert.Equal(original.PingStatus, deserialized.PingStatus);
        Assert.Equal(original.EditMode, deserialized.EditMode);
        Assert.Equal(original.Categories, deserialized.Categories);
        Assert.Equal(original.Tags, deserialized.Tags);
        Assert.NotNull(deserialized.Meta);
        Assert.Equal("custom_value", deserialized.Meta["_custom_field"]?.ToString());
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("future")]
    [InlineData("draft")]
    [InlineData("pending")]
    [InlineData("private")]
    public void ValidatePostMetadata_ValidStatuses_DoesNotThrow(string validStatus)
    {
        var meta = new EditablePostMetadata { Status = validStatus };
        CacheService.ValidatePostMetadata(meta, "1-test.md");
    }

    [Fact]
    public void ValidatePostMetadata_InvalidStatus_ThrowsInvalidOperationException()
    {
        var meta = new EditablePostMetadata { Status = "archived" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CacheService.ValidatePostMetadata(meta, "1-test.md"));

        Assert.Contains("Invalid value 'archived' for 'status'", ex.Message);
    }

    [Fact]
    public void ValidatePostMetadata_ValidDate_DoesNotThrow()
    {
        var meta = new EditablePostMetadata { Date = "2026-10-04 15:30:00" };
        CacheService.ValidatePostMetadata(meta, "1-test.md");
    }

    [Fact]
    public void ValidatePostMetadata_InvalidDate_ThrowsInvalidOperationException()
    {
        var meta = new EditablePostMetadata { Date = "invalid-date-string" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CacheService.ValidatePostMetadata(meta, "1-test.md"));

        Assert.Contains("Invalid format for 'date'", ex.Message);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("closed")]
    public void ValidatePostMetadata_ValidCommentAndPingStatus_DoesNotThrow(string status)
    {
        var meta = new EditablePostMetadata
        {
            CommentStatus = status,
            PingStatus = status
        };
        CacheService.ValidatePostMetadata(meta, "1-test.md");
    }

    [Fact]
    public void ValidatePostMetadata_InvalidCommentStatus_Throws()
    {
        var meta = new EditablePostMetadata { CommentStatus = "forbidden" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CacheService.ValidatePostMetadata(meta, "1-test.md"));

        Assert.Contains("commentStatus", ex.Message);
    }

    [Fact]
    public void ValidatePostMetadata_InvalidPingStatus_Throws()
    {
        var meta = new EditablePostMetadata { PingStatus = "forbidden" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CacheService.ValidatePostMetadata(meta, "1-test.md"));

        Assert.Contains("pingStatus", ex.Message);
    }

    [Theory]
    [InlineData("markdown")]
    [InlineData("html")]
    public void ValidatePostMetadata_ValidEditMode_DoesNotThrow(string mode)
    {
        var meta = new EditablePostMetadata { EditMode = mode };
        CacheService.ValidatePostMetadata(meta, "1-test.md");
    }

    [Fact]
    public void ValidatePostMetadata_InvalidEditMode_Throws()
    {
        var meta = new EditablePostMetadata { EditMode = "rich-text" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CacheService.ValidatePostMetadata(meta, "1-test.md"));

        Assert.Contains("editMode", ex.Message);
    }

    [Fact]
    public void ReadLocalPost_NonExistentFile_ReturnsNull()
    {
        var result = _cacheService.ReadLocalPost(99999);
        Assert.Null(result);
    }

    [Fact]
    public void ReadLocalPost_ValidFileWithFrontMatter_ParsesMetadataAndBody()
    {
        var postDir = Path.Combine(_tempDir, "test-conn", "posts", "publish");
        Directory.CreateDirectory(postDir);

        var filePath = Path.Combine(postDir, "100-sample-article.md");
        var markdownFileContent = """
---
title: Sample Article
status: publish
slug: sample-article
---
# Hello World

This is the body content.
""";
        File.WriteAllText(filePath, markdownFileContent);

        var localPost = _cacheService.ReadLocalPost(100);

        Assert.NotNull(localPost);
        Assert.NotNull(localPost.Metadata);
        Assert.Equal("Sample Article", localPost.Metadata.Title);
        Assert.Equal("publish", localPost.Metadata.Status);
        Assert.Equal("sample-article", localPost.Metadata.Slug);
        Assert.Contains("# Hello World", localPost.Content);
        Assert.Contains("This is the body content.", localPost.Content);
    }

    [Fact]
    public void ReadLocalPost_FileWithoutFrontMatter_TreatsAllAsContent()
    {
        var postDir = Path.Combine(_tempDir, "test-conn", "posts", "draft");
        Directory.CreateDirectory(postDir);

        var filePath = Path.Combine(postDir, "200-plain-notes.md");
        var plainContent = "# Plain Notes\nJust raw notes without YAML header.";
        File.WriteAllText(filePath, plainContent);

        var localPost = _cacheService.ReadLocalPost(200);

        Assert.NotNull(localPost);
        Assert.Equal(plainContent, localPost.Content);
    }
}
