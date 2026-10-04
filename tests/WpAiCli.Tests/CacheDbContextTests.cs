using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WpAiCli.Services;
using WpAiCli.Services.Data;
using Xunit;

namespace WpAiCli.Tests;

public class CacheDbContextTests : IDisposable
{
    private readonly string _tempDbDir;
    private readonly string _dbPath;

    public CacheDbContextTests()
    {
        _tempDbDir = Path.Combine(Path.GetTempPath(), "wpai_db_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDbDir);
        _dbPath = Path.Combine(_tempDbDir, "test-cache.db");
        CacheDbContext.ResetInitializationCache();
    }

    public void Dispose()
    {
        CacheDbContext.ResetInitializationCache();
        // Clear all connection pools so SQLite releases file lock on Windows/macOS
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDbDir))
        {
            try { Directory.Delete(_tempDbDir, true); } catch { }
        }
    }

    [Fact]
    public void Constructor_CreatesDatabaseAndTables_Successfully()
    {
        using (var db = new CacheDbContext(_dbPath))
        {
            // Verify file exists
            Assert.True(File.Exists(_dbPath));

            // Verify tables can be queried and modified
            db.Posts.Add(new CachedPost
            {
                PostId = 1,
                Title = "Test Post",
                Slug = "test-post",
                Status = "publish",
                Date = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                ServerLastModified = DateTime.UtcNow
            });

            db.Categories.Add(new CachedCategory
            {
                Id = 10,
                Name = "Tech",
                Slug = "tech"
            });

            db.Tags.Add(new CachedTag
            {
                Id = 20,
                Name = "C#",
                Slug = "csharp"
            });

            db.States.Add(new CacheState
            {
                Key = "test_key",
                Value = "test_value"
            });

            db.Media.Add(new CachedMedia
            {
                MediaId = 30,
                FileName = "image.png",
                LastModified = DateTime.UtcNow,
                ServerLastModified = DateTime.UtcNow
            });

            db.SaveChanges();
        }

        // Re-read in fresh context instance
        using (var db2 = new CacheDbContext(_dbPath))
        {
            var post = db2.Posts.Find(1);
            Assert.NotNull(post);
            Assert.Equal("Test Post", post.Title);

            var category = db2.Categories.Find(10);
            Assert.NotNull(category);
            Assert.Equal("Tech", category.Name);

            var state = db2.States.Find("test_key");
            Assert.NotNull(state);
            Assert.Equal("test_value", state.Value);
        }
    }

    [Fact]
    public void Constructor_MultipleInstances_InitializesWithoutErrors()
    {
        // Calling multiple times should use ConcurrentDictionary initialization cache
        using var db1 = new CacheDbContext(_dbPath);
        using var db2 = new CacheDbContext(_dbPath);
        using var db3 = new CacheDbContext(_dbPath);

        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public void Pragma_JournalMode_IsWal()
    {
        using var db = new CacheDbContext(_dbPath);

        using var command = db.Database.GetDbConnection().CreateCommand();
        db.Database.OpenConnection();
        command.CommandText = "PRAGMA journal_mode;";
        var result = command.ExecuteScalar()?.ToString();

        // In SQLite with WAL enabled, journal_mode returns "wal"
        Assert.Equal("wal", result, ignoreCase: true);
    }

    [Fact]
    public void CacheService_Dispose_ReleasesUnderlyingDbContext()
    {
        var cacheService = new CacheService(_tempDbDir, "conn1");
        cacheService.SetState("ping", "pong");

        // Calling Dispose multiple times is safe and idempotently cleans up
        cacheService.Dispose();
        cacheService.Dispose();
    }

    [Fact]
    public async Task CacheService_DisposeAsync_ReleasesUnderlyingDbContext()
    {
        var cacheService = new CacheService(_tempDbDir, "conn2");
        cacheService.SetState("async_ping", "pong");

        // Calling DisposeAsync multiple times is safe
        await cacheService.DisposeAsync();
        await cacheService.DisposeAsync();
    }
}
