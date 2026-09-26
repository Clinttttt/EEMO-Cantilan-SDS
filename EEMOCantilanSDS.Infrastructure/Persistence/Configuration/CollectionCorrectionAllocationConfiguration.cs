using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class CollectionCorrectionAllocationConfiguration : IEntityTypeConfiguration<CollectionCorrectionAllocation>
{
    public void Configure(EntityTypeBuilder<CollectionCorrectionAllocation> builder)
    {
        builder.ToTable("CollectionCorrectionAllocations", table => table.HasCheckConstraint(
            "CK_CollectionCorrectionAllocations_Effect_NonZero", "\"FinancialEffectAmount\" <> 0"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.CorrectionLineId).IsRequired();
        builder.Property(x => x.OriginalAllocationId).IsRequired();
        builder.Property(x => x.FinancialEffectAmount).HasPrecision(18, 2).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.CorrectionLineId });
        builder.HasOne<CollectionCorrectionLine>().WithMany(x => x.Allocations)
            .HasForeignKey(x => new { x.MunicipalityId, x.CorrectionLineId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollectionAllocation>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.OriginalAllocationId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
