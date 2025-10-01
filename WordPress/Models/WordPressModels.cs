using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpAiCli.WordPress.Models;

[JsonConverter(typeof(WordPressRenderedContentConverter))]
public sealed class WordPressRenderedContent
{
    [JsonPropertyName("raw")]
    public string? Raw { get; set; }

    [JsonPropertyName("rendered")]
    public string? Rendered { get; set; }

    public override string ToString() => Rendered ?? Raw ?? string.Empty;
}

public interface IHasTitle
{
    WordPressRenderedContent? Title { get; }
    string? Slug { get; }
}

public class WordPressPostBase : IHasTitle
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }

    [JsonPropertyName("date_gmt")]
    public DateTime? DateGmt { get; set; }

    [JsonPropertyName("guid")]
    public WordPressRenderedContent? Guid { get; set; }

    [JsonPropertyName("modified")]
    public DateTime? Modified { get; set; }

    [JsonPropertyName("modified_gmt")]
    public DateTime? ModifiedGmt { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("title")]
    public WordPressRenderedContent? Title { get; set; }
}

public sealed class WordPressPostSummary : WordPressPostBase
{
}

public sealed class WordPressPostDetail : WordPressPostBase
{
    [JsonPropertyName("content")]
    public WordPressRenderedContent? Content { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("author")]
    public int? Author { get; set; }

    [JsonPropertyName("featured_media")]
    public int? FeaturedMedia { get; set; }

    [JsonPropertyName("comment_status")]
    public string? CommentStatus { get; set; }

    [JsonPropertyName("ping_status")]
    public string? PingStatus { get; set; }

    [JsonPropertyName("sticky")]
    public bool? Sticky { get; set; }

    [JsonPropertyName("template")]
    public string? Template { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("categories")]
    public IReadOnlyList<int>? Categories { get; set; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<int>? Tags { get; set; }

    [JsonPropertyName("permalink_template")]
    public string? PermalinkTemplate { get; set; }

    [JsonPropertyName("generated_slug")]
    public string? GeneratedSlug { get; set; }

    [JsonPropertyName("class_list")]
    public IReadOnlyList<string>? ClassList { get; set; }

    [JsonPropertyName("excerpt")]
    public WordPressRenderedContent? Excerpt { get; set; }
}

public sealed class WordPressCreatePostRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("categories")]
    public IList<int>? Categories { get; set; }

    [JsonPropertyName("tags")]
    public IList<int>? Tags { get; set; }

    [JsonPropertyName("featured_media")]
    public int? FeaturedMedia { get; set; }
}

public sealed class WordPressUpdatePostRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("categories")]
    public IList<int>? Categories { get; set; }

    [JsonPropertyName("tags")]
    public IList<int>? Tags { get; set; }

    [JsonPropertyName("featured_media")]
    public int? FeaturedMedia { get; set; }

    [JsonPropertyName("comment_status")]
    public string? CommentStatus { get; set; }

    [JsonPropertyName("ping_status")]
    public string? PingStatus { get; set; }

    [JsonPropertyName("sticky")]
    public bool? Sticky { get; set; }

    [JsonPropertyName("template")]
    public string? Template { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }

    [JsonPropertyName("excerpt")]
    public string? Excerpt { get; set; }
}

public sealed class WordPressRevision : IHasTitle
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("author")]
    public int Author { get; set; }

    [JsonPropertyName("date_gmt")]
    public DateTime DateGmt { get; set; }

    [JsonPropertyName("modified_gmt")]
    public DateTime ModifiedGmt { get; set; }

    [JsonPropertyName("parent")]
    public int Parent { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("title")]
    public WordPressRenderedContent? Title { get; set; }

    [JsonPropertyName("content")]
    public WordPressRenderedContent? Content { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class WordPressCategory
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("parent")]
    public int? Parent { get; set; }
}

public sealed class WordPressTag
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }
}

public sealed class WordPressCreateTagRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class WordPressMediaItem : IHasTitle
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }

    [JsonPropertyName("title")]
    public WordPressRenderedContent? Title { get; set; }

    [JsonIgnore]
    public string? Slug => null;

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }
}

public sealed class WordPressDeleteResponse
{
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("previous")]
    public Dictionary<string, JsonElement>? Previous { get; set; }
}
