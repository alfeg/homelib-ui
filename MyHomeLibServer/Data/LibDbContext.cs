using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyHomeLib.Library;
using System.Reflection;

namespace MyHomeLibServer.Data;

public class LibDbContext : DbContext
{
    public DbSet<BookItem> BookItems { get; set; }
    public DbSet<SyncState> SyncStates { get; set; }

    public string DbPath { get; set; }

    public LibDbContext(IOptions<LibraryConfig>? options = null)
    {
        if (options?.Value.DbPath == null)
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);

            var indexFile = Path.GetFileName(options?.Value.CatalogIndexFile ?? "design.db");
            
            DbPath = System.IO.Path.Join(path, "HomeLibUi", Path.ChangeExtension(indexFile, ".db"));
        }
        else
        {
            DbPath = options.Value.DbPath;
        }

        var dirName = Path.GetDirectoryName(DbPath);
        if (!Directory.Exists(dirName))
        {
            Directory.CreateDirectory(dirName);
        }
    }

    // The following configures EF to create a Sqlite database file in the
    // special "local" folder for your platform.
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.Entity<SyncState>().HasKey(x => x.Id);
        modelBuilder.Entity<SyncState>().Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .IsRequired();

        base.OnModelCreating(modelBuilder);
    }
}

public class SyncState
{
    public long Id { get; set; }
    public string InpxFile { get; set; }
    public string Etag { get; set; }
    public long DurationMs { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public bool IsSynced { get; set; }
}
