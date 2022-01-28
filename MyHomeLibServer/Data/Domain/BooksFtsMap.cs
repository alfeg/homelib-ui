using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyHomeLibServer.Data.Domain;

public class BooksFtsMap : IEntityTypeConfiguration<BooksFts>
{
    public void Configure(EntityTypeBuilder<BooksFts> map)
    {
        map.HasKey(m => m.RowId);
        map.ToTable("books_fts");
        map.Property(x => x.Match).HasColumnName("books_fts");
        map.HasOne(x => x.Book).WithOne(p => p.FTS)
            .HasForeignKey<BooksFts>(b => b.RowId);

    }
}