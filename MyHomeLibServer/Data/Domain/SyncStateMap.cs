using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyHomeLibServer.Data.Domain;

public class SyncStateMap : IEntityTypeConfiguration<SyncState>
{
    public void Configure(EntityTypeBuilder<SyncState> map)
    {
        map.HasKey(x => x.Id);
        map.Property(x => x.Id).ValueGeneratedOnAdd().IsRequired();
    }
}