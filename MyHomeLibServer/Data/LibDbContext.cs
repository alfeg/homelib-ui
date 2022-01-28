using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Reflection;
using MyHomeLibServer.Data.Domain;

namespace MyHomeLibServer.Data;

public class LibDbContext : DbContext
{
    public DbSet<Book> Books { get; set; } = null!;
    public DbSet<BooksFts> BooksFts { get; set; } = null!;
    public DbSet<Author> Authors { get; set; } = null!;
    public DbSet<Series> Series { get; set; } = null!;
    public DbSet<SyncState> SyncStates { get; set; } = null!;

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
        if (dirName != null && !Directory.Exists(dirName))
        {
            Directory.CreateDirectory(dirName);
        }
    }

    // The following configures EF to create a Sqlite database file in the
    // special "local" folder for your platform.
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={DbPath}");
            //.LogTo(Console.WriteLine);

        options.EnableSensitiveDataLogging();
    }

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