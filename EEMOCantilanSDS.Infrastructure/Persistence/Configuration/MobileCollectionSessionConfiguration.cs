using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
public sealed class MobileCollectionSessionConfiguration : IEntityTypeConfiguration<MobileCollectionSession>
{
    public void Configure(EntityTypeBuilder<MobileCollectionSession> b)
    {
        b.ToTable("MobileCollectionSessions");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.MunicipalityId, x.ClientCollectionSessionId }).IsUnique();
        b.Property(x => x.IntentFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
    }
}
