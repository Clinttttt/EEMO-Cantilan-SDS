using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class SpaceHolderImportRunConfiguration : IEntityTypeConfiguration<SpaceHolderImportRun>
{
    public void Configure(EntityTypeBuilder<SpaceHolderImportRun> b)
    {
        b.ToTable("SpaceHolderImportRuns", t => t.HasCheckConstraint("CK_SpaceHolderImportRun_Counts",
            "\"RowCount\" >= 1 AND \"ImportedCount\" >= 0 AND \"SkippedCount\" >= 0 AND \"ImportedCount\" + \"SkippedCount\" <= \"RowCount\""));
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.MunicipalityId, x.Kind, x.OccurredAtUtc });
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique().HasFilter("\"ClientOperationId\" IS NOT NULL");
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.ActorName).HasMaxLength(100);
        b.Property(x => x.SkippedSummary).HasMaxLength(1000);
    }
}
