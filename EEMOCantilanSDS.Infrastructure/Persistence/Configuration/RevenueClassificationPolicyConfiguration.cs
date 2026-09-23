using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class RevenueClassificationPolicyConfiguration : IEntityTypeConfiguration<RevenueClassificationPolicy>
{
    public void Configure(EntityTypeBuilder<RevenueClassificationPolicy> builder)
    {
        builder.ToTable("RevenueClassificationPolicies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.RevenueClassificationId).IsRequired();
        builder.Property(x => x.EffectiveDate).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.PermittedInstrumentType).HasConversion<int?>();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);

        // A policy version is immutable and deliberately has no soft-delete lifecycle: hiding an old
        // version would make the policy in effect on a historical date unreproducible.
        builder.HasIndex(x => new { x.MunicipalityId, x.RevenueClassificationId, x.EffectiveDate })
            .IsUnique();

        // Including MunicipalityId in the FK makes cross-tenant classification references impossible
        // even for callers that bypass the normal tenant query filter.
        builder.HasOne<RevenueClassification>()
            .WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.RevenueClassificationId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
