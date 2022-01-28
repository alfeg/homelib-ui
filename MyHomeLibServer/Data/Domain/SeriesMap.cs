using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyHomeLibServer.Data.Domain;

public class SeriesMap : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> map)
    {
        map.HasKey(x => x.Id);

        map.Property(x => x.Id).ValueGeneratedNever();
        map.Property(x => x.Name);
        map.HasIndex(x => x.Name);
    }
}