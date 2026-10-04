using WpAiCli.Parsing;
using Xunit;

namespace WpAiCli.Tests;

public class OptionParserTests
{
    [Fact]
    public void Parse_EmptyArgs_ReturnsEmptyPositionalsAndNoOptions()
    {
        var parsed = OptionParser.Parse(Array.Empty<string>());

        Assert.False(parsed.HasOptions);
        Assert.Empty(parsed.Positionals);
    }

    [Fact]
    public void Parse_PositionalsOnly_PreservesOrder()
    {
        var parsed = OptionParser.Parse(new[] { "posts", "list", "recent" });

        Assert.False(parsed.HasOptions);
        Assert.Equal(3, parsed.Positionals.Count);
        Assert.Equal("posts", parsed.Positionals[0]);
        Assert.Equal("list", parsed.Positionals[1]);
        Assert.Equal("recent", parsed.Positionals[2]);
    }

    [Fact]
    public void Parse_FlagWithoutValue_DefaultsToTrue()
    {
        var parsed = OptionParser.Parse(new[] { "--dry-run" });

        Assert.True(parsed.HasOptions);
        Assert.Equal("true", parsed.GetString("dry-run"));
        Assert.True(parsed.GetBool("dry-run", false));
    }

    [Fact]
    public void Parse_SeparateKeyValue_ParsesCorrectly()
    {
        var parsed = OptionParser.Parse(new[] { "--status", "publish" });

        Assert.True(parsed.HasOptions);
        Assert.Equal("publish", parsed.GetString("status"));
        Assert.Empty(parsed.Positionals);
    }

    [Fact]
    public void Parse_EqualKeyValue_ParsesCorrectly()
    {
        var parsed = OptionParser.Parse(new[] { "--status=draft" });

        Assert.True(parsed.HasOptions);
        Assert.Equal("draft", parsed.GetString("status"));
    }

    [Fact]
    public void Parse_EqualKeyWithEmptyValue_ParsesAsEmptyString()
    {
        var parsed = OptionParser.Parse(new[] { "--title=" });

        Assert.True(parsed.HasOptions);
        Assert.Equal(string.Empty, parsed.GetString("title"));
    }

    [Fact]
    public void Parse_ConsecutiveFlags_BothParsedAsTrue()
    {
        var parsed = OptionParser.Parse(new[] { "--force", "--dry-run" });

        Assert.True(parsed.GetBool("force", false));
        Assert.True(parsed.GetBool("dry-run", false));
    }

    [Fact]
    public void Parse_MixOfPositionalsAndOptions_SeparatesProperly()
    {
        var parsed = OptionParser.Parse(new[] { "posts", "--id", "42", "publish" });

        Assert.Equal(new[] { "posts", "publish" }, parsed.Positionals);
        Assert.Equal(42, parsed.GetInt("id"));
    }

    [Fact]
    public void GetString_CaseInsensitive_ReturnsValue()
    {
        var parsed = OptionParser.Parse(new[] { "--Status", "draft" });

        Assert.Equal("draft", parsed.GetString("status"));
        Assert.Equal("draft", parsed.GetString("STATUS"));
        Assert.Equal("draft", parsed.GetString("Status"));
    }

    [Fact]
    public void GetString_NonExistentKey_ReturnsNull()
    {
        var parsed = OptionParser.Parse(new[] { "--id", "10" });

        Assert.Null(parsed.GetString("name"));
    }

    [Fact]
    public void GetInt_ValidNumber_ReturnsInteger()
    {
        var parsed = OptionParser.Parse(new[] { "--id", "123" });

        Assert.Equal(123, parsed.GetInt("id"));
    }

    [Fact]
    public void GetInt_InvalidNumber_ReturnsNull()
    {
        var parsed = OptionParser.Parse(new[] { "--id", "abc" });

        Assert.Null(parsed.GetInt("id"));
    }

    [Fact]
    public void GetInt_NonExistent_ReturnsNull()
    {
        var parsed = OptionParser.Parse(Array.Empty<string>());

        Assert.Null(parsed.GetInt("id"));
    }

    [Fact]
    public void GetIntArray_CommaSeparated_ReturnsArray()
    {
        var parsed = OptionParser.Parse(new[] { "--ids", "1,2,3" });

        var result = parsed.GetIntArray("ids");
        Assert.NotNull(result);
        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void GetIntArray_SemicolonSeparated_ReturnsArray()
    {
        var parsed = OptionParser.Parse(new[] { "--ids", "10;20;30" });

        var result = parsed.GetIntArray("ids");
        Assert.NotNull(result);
        Assert.Equal(new[] { 10, 20, 30 }, result);
    }

    [Fact]
    public void GetIntArray_InvalidEntriesIgnored()
    {
        var parsed = OptionParser.Parse(new[] { "--ids", "1,abc,3" });

        var result = parsed.GetIntArray("ids");
        Assert.NotNull(result);
        Assert.Equal(new[] { 1, 3 }, result);
    }

    [Fact]
    public void GetIntArray_NonExistent_ReturnsNull()
    {
        var parsed = OptionParser.Parse(Array.Empty<string>());

        Assert.Null(parsed.GetIntArray("ids"));
    }

    [Fact]
    public void GetBool_ExplicitTrue_ReturnsTrue()
    {
        var parsed = OptionParser.Parse(new[] { "--all", "true" });

        Assert.True(parsed.GetBool("all", false));
    }

    [Fact]
    public void GetBool_ExplicitFalse_ReturnsFalse()
    {
        var parsed = OptionParser.Parse(new[] { "--all", "false" });

        Assert.False(parsed.GetBool("all", true));
    }

    [Fact]
    public void GetBool_NotSpecified_ReturnsDefaultValue()
    {
        var parsed = OptionParser.Parse(Array.Empty<string>());

        Assert.True(parsed.GetBool("missing", true));
        Assert.False(parsed.GetBool("missing", false));
    }

    [Fact]
    public void DuplicateKeys_LastValueWins()
    {
        var parsed = OptionParser.Parse(new[] { "--status", "draft", "--status", "publish" });

        Assert.Equal("publish", parsed.GetString("status"));
    }
}
