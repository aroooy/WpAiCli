using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WpAiCli.Services.Data;

public class CachedPost
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int PostId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public string RawPostJson { get; set; } = string.Empty;
    public DateTime ServerLastModified { get; set; }
    public DateTime LastModified { get; set; }
}

public class CachedCategory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class CachedTag
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class CacheState
{
    [Key]
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class CachedMedia
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int MediaId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string MetadataHash { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public DateTime ServerLastModified { get; set; }
    public string RawMediaJson { get; set; } = string.Empty;
}

public class CacheDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<CachedPost> Posts { get; set; }
    public DbSet<CachedCategory> Categories { get; set; }
    public DbSet<CachedTag> Tags { get; set; }
    public DbSet<CacheState> States { get; set; }
    public DbSet<CachedMedia> Media { get; set; }

    public CacheDbContext(string dbPath)
    {
        _dbPath = dbPath;
        Database.EnsureCreated();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }
}
