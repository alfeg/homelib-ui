using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyHomeLibServer.Data.Domain;

public class KeywordsMap : IEntityTypeConfiguration<Keyword>
{
    public void Configure(EntityTypeBuilder<Keyword> map)
    {
        map.HasKey(x => x.Id);
        map.Property(x => x.Key);
    }
}