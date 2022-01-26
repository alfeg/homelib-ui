using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyHomeLib.Library;

namespace MyHomeLibServer.Data;

public class BookItemEntityMap : IEntityTypeConfiguration<BookItem>
{
    public void Configure(EntityTypeBuilder<BookItem> map)
    {
        map.HasKey(x => x.Id);

        map.ToTable("books");

        map.Property(x => x.Title);
        map.HasIndex(x => x.Title);

        map.Property(x => x.Authors);
        map.HasIndex(x => x.Authors);

        map.Property(x => x.ArchiveFile);
        map.Property(x => x.Deleted);
        map.Property(x => x.Ext);
        map.Property(x => x.File);
        map.Property(x => x.Genre);
        map.HasIndex(x => x.Genre);

        map.Property(x => x.Keywords);
        map.HasIndex(x => x.Keywords);

        map.Property(x => x.Lang);
        map.Property(x => x.LibId);
        map.Property(x => x.LibRate);
        map.Property(x => x.Size);
        map.Property(x => x.Series);
        map.HasIndex(x => x.Series);

        map.Property(x => x.Date);
    }
}