using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyHomeLibServer.Data.Domain;

public class AuthorMap : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> map)
    {
        map.HasKey(x => x.Id);

        map.Property(x => x.Id).ValueGeneratedNever().IsRequired();
        map.Property(x => x.FirstName);
        map.Property(x => x.MiddleName);
        map.Property(x => x.LastName);

        map.HasMany(x => x.Books).WithMany(x => x.Authors);
        map.HasMany(x => x.Series).WithMany(x => x.Authors);
        map.HasIndex(nameof(Author.LastName), nameof(Author.FirstName), nameof(Author.MiddleName));
    }
}