using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class OfficialReportRevisionConfiguration : IEntityTypeConfiguration<OfficialReportRevision>
{
    public void Configure(EntityTypeBuilder<OfficialReportRevision> b)
    {
        b.ToTable("OfficialReportRevisions", t =>
        {
            t.HasCheckConstraint("CK_OfficialReportRevision_Scope", "\"Year\" BETWEEN 2000 AND 2200 AND \"Revision\" > 0 AND ((\"Kind\" = 1 AND \"Month\" = 0 AND \"Amount\" >= 0) OR (\"Kind\" = 2 AND \"Month\" BETWEEN 1 AND 12 AND \"SystemAmountAtRevision\" IS NOT NULL))");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.RowKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.IntentFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.SourceOrReason).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.ActorName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.SystemAmountAtRevision).HasPrecision(18, 2);
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        b.HasIndex(x => new { x.MunicipalityId, x.Kind, x.RowKey, x.Year, x.Month, x.Revision }).IsUnique();
    }
}
