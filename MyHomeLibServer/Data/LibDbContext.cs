using Microsoft.EntityFrameworkCore;
using MyHomeLib.Library;
using System.Reflection;

namespace MyHomeLibServer.Data;

public class LibDbContext : DbContext
{
    public DbSet<BookItem> BookItems { get; set; }

    public string DbPath { get; set; }

    public LibDbContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        DbPath = System.IO.Path.Join(path, "myhomelib.db");
    }

    // The following configures EF to create a Sqlite database file in the
    // special "local" folder for your platform.
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
