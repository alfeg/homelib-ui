using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyHomeLibServer.Data.Domain;

public class BookMap : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> map)
    {
        map.HasKey(m => m.Id);

        map.Property(m => m.Id).ValueGeneratedNever();
        map.Property(x => x.Title);
        map.HasIndex(x => x.Title);
        map.Property(x => x.ArchiveFile);
        map.Property(x => x.FileName);
        map.Property(x => x.Extension);
        map.Property(x => x.Size);
        map.Property(x => x.SeriesNo);
        map.HasMany(x => x.Authors).WithMany(x => x.Books);
        map.HasMany(x => x.Genres);
        map.HasMany(x => x.Keywords);
        map.HasOne(x => x.Series);
        
    }
}