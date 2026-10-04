using WpAiCli.Services;
using Xunit;

namespace WpAiCli.Tests;

public class SanitizeTitleTests
{
    [Theory]
    [InlineData(null, "untitled")]
    [InlineData("", "untitled")]
    [InlineData("   ", "untitled")]
    [InlineData("\t\n\r", "untitled")]
    public void Sanitize_NullOrWhitespace_ReturnsUntitled(string? input, string expected)
    {
        var result = CacheService.SanitizeTitleForFilename(input!);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Sanitize_StandardAscii_ReplacesSpacesWithHyphens()
    {
        var result = CacheService.SanitizeTitleForFilename("Hello World Test");
        Assert.Equal("Hello-World-Test", result);
    }

    [Fact]
    public void Sanitize_JapaneseCharacters_Preserved()
    {
        var result = CacheService.SanitizeTitleForFilename("WordPressの最新記事について");
        Assert.Equal("WordPressの最新記事について", result);
    }

    [Fact]
    public void Sanitize_FullWidthSpaces_ReplacedWithHyphens()
    {
        var result = CacheService.SanitizeTitleForFilename("こんにちは　世界");
        Assert.Equal("こんにちは-世界", result);
    }

    [Fact]
    public void Sanitize_HtmlEntities_DecodesBeforeSanitizing()
    {
        // &amp; -> &
        var resultAmp = CacheService.SanitizeTitleForFilename("Tom &amp; Jerry");
        Assert.Equal("Tom-&-Jerry", resultAmp);

        // &#8211; is en-dash (–), which is not an invalid filename char, but hyphens collapse
        var resultDash = CacheService.SanitizeTitleForFilename("Part 1 &#8211; Introduction");
        Assert.Contains("Introduction", resultDash);

        // &quot;Quotes&quot; -> "Quotes" -> quotes are invalid filename chars, replaced with -
        var resultQuotes = CacheService.SanitizeTitleForFilename("&quot;Quotes&quot;");
        Assert.Equal("Quotes", resultQuotes);
    }

    [Fact]
    public void Sanitize_InvalidFileNameChars_ReplacedWithHyphens()
    {
        // Invalid chars: / \ : * ? " < > |
        var result = CacheService.SanitizeTitleForFilename("Title / With \\ Invalid : Chars * And ? Quotes \" <brackets> | pipe");
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("\\", result);
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("?", result);
        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain("|", result);
    }

    [Fact]
    public void Sanitize_ConsecutiveHyphens_CollapsedToOne()
    {
        var result = CacheService.SanitizeTitleForFilename("A----B----C");
        Assert.Equal("A-B-C", result);
    }

    [Fact]
    public void Sanitize_LeadingAndTrailingHyphens_Trimmed()
    {
        var result = CacheService.SanitizeTitleForFilename("---hello-world---");
        Assert.Equal("hello-world", result);
    }

    [Fact]
    public void Sanitize_OnlyInvalidChars_ReturnsUntitled()
    {
        var result = CacheService.SanitizeTitleForFilename("///:::???***");
        Assert.Equal("untitled", result);
    }

    [Fact]
    public void Sanitize_LongTitle_TruncatedTo100Chars()
    {
        var longTitle = new string('a', 150);
        var result = CacheService.SanitizeTitleForFilename(longTitle);

        Assert.Equal(100, result.Length);
        Assert.Equal(new string('a', 100), result);
    }

    [Fact]
    public void Sanitize_LongTitleEndingWithHyphen_TrimmedBelow100Chars()
    {
        // 99 'a's, followed by a hyphen at index 99 (100th char), followed by more chars
        var longTitle = new string('a', 99) + "-extra-long-suffix";
        var result = CacheService.SanitizeTitleForFilename(longTitle);

        Assert.True(result.Length <= 100);
        Assert.False(result.EndsWith('-'));
        Assert.Equal(new string('a', 99), result);
    }
}
