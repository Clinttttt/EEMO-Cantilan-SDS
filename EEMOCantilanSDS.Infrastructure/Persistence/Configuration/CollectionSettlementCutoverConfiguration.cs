using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class CollectionSettlementCutoverConfiguration : IEntityTypeConfiguration<CollectionSettlementCutover>
{
    public void Configure(EntityTypeBuilder<CollectionSettlementCutover> builder)
    {
        builder.ToTable("CollectionSettlementCutovers", table =>
        {
            table.HasCheckConstraint(
                "CK_CollectionSettlementCutovers_OpeningAmounts",
                "\"OpeningAssessmentAmount\" >= 0 AND \"OpeningLegacySettledAmount\" >= 0 " +
                "AND \"OpeningOutstandingAmount\" >= 0 " +
                "AND \"OpeningAssessmentAmount\" = \"OpeningLegacySettledAmount\" + \"OpeningOutstandingAmount\"");
            table.HasCheckConstraint("CK_CollectionSettlementCutovers_BoundaryVersion_Positive", "\"BoundaryVersion\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.SourceKind).HasConversion<int>().IsRequired();
        builder.Property(x => x.SourceId).IsRequired();
        builder.Property(x => x.SourcePart).HasConversion<int?>();
        builder.Property(x => x.BoundaryVersion).IsRequired();
        builder.Property(x => x.CutoverAtUtc).IsRequired();
        builder.Property(x => x.OpeningAssessmentAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.OpeningLegacySettledAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.OpeningOutstandingAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ReconciliationEvidence).HasColumnType("text").IsRequired();
        builder.Property(x => x.ReconciledByUserId).IsRequired();
        builder.Property(x => x.ReconciledAtUtc).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.SourceKind, x.SourceId })
            .IsUnique().HasFilter("\"SourcePart\" IS NULL");
        builder.HasIndex(x => new { x.MunicipalityId, x.SourceKind, x.SourceId, x.SourcePart })
            .IsUnique().HasFilter("\"SourcePart\" IS NOT NULL");
    }
}
