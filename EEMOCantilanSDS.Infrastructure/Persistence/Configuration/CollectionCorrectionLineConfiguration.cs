using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class CollectionCorrectionLineConfiguration : IEntityTypeConfiguration<CollectionCorrectionLine>
{
    public void Configure(EntityTypeBuilder<CollectionCorrectionLine> builder)
    {
        builder.ToTable("CollectionCorrectionLines", table => table.HasCheckConstraint(
            "CK_CollectionCorrectionLines_Effect_NonZero", "\"FinancialEffectAmount\" <> 0"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.CorrectionId).IsRequired();
        builder.Property(x => x.OriginalCollectionLineId).IsRequired();
        builder.Property(x => x.FinancialEffectAmount).HasPrecision(18, 2).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.CorrectionId });
        builder.HasOne<CollectionCorrection>().WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.MunicipalityId, x.CorrectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollectionLine>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.OriginalCollectionLineId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
