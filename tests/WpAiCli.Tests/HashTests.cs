using System.Security.Cryptography;
using System.Text;
using WpAiCli.Services;
using WpAiCli.Services.Data;
using Xunit;

namespace WpAiCli.Tests;

public class HashTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CacheService _cacheService;

    public HashTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wpai_hash_tests_" + Guid.NewGuid().ToString("N"));
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
    public void ComputeSha256Hash_EmptyString_MatchesStandardSha256()
    {
        // Standard SHA-256 for empty string: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        const string expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var actual = _cacheService.ComputeSha256Hash(string.Empty);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputeSha256Hash_String_MatchesDotNetStandardImplementation()
    {
        const string input = "Hello, WordPress! 🚀 日本語テスト";

        using var sha256 = SHA256.Create();
        var expectedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        var expected = string.Concat(expectedBytes.Select(b => b.ToString("x2")));

        var actual = _cacheService.ComputeSha256Hash(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputeSha256Hash_SameInput_AlwaysProducesIdenticalHash()
    {
        const string input = "Determinism check with identical inputs";

        var hash1 = _cacheService.ComputeSha256Hash(input);
        var hash2 = _cacheService.ComputeSha256Hash(input);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeSha256Hash_DifferentInput_ProducesDifferentHash()
    {
        var hash1 = _cacheService.ComputeSha256Hash("Content Version A");
        var hash2 = _cacheService.ComputeSha256Hash("Content Version B");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeSha256Hash_ByteArray_MatchesUtf8String()
    {
        const string text = "Binary and string equality check";
        var bytes = Encoding.UTF8.GetBytes(text);

        var hashString = _cacheService.ComputeSha256Hash(text);
        var hashBytes = _cacheService.ComputeSha256Hash(bytes);

        Assert.Equal(hashString, hashBytes);
    }

    [Fact]
    public void ComputeSha256Hash_EmptyByteArray_MatchesKnownHash()
    {
        const string expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var actual = _cacheService.ComputeSha256Hash(Array.Empty<byte>());

        Assert.Equal(expected, actual);
    }
}
