using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyHomeLibServer.Data.Domain;

public class GenreMap : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> map)
    {
        map.HasKey(x => x.Id);
        map.Property(x => x.Name);
    }
}