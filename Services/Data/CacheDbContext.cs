
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
    public string ContentHash { get; set; } = string.Empty;
    public string EditableMetaHash { get; set; } = string.Empty;
    public string RawPostJson { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public class CacheDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<CachedPost> Posts { get; set; }

    public CacheDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }
}
