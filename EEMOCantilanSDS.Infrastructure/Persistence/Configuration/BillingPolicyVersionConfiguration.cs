using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class BillingPolicyVersionConfiguration : IEntityTypeConfiguration<BillingPolicyVersion>
{
    public void Configure(EntityTypeBuilder<BillingPolicyVersion> b)
    {
        b.ToTable("BillingPolicyVersions", t =>
        {
            t.HasCheckConstraint("CK_BillingPolicy_DueDay", "\"DueDay\" IS NULL OR \"DueDay\" BETWEEN 1 AND 31");
            t.HasCheckConstraint("CK_BillingPolicy_Grace", "\"GraceDays\" BETWEEN 0 AND 90");
            t.HasCheckConstraint("CK_BillingPolicy_Advance", "\"MaxAdvanceMonths\" BETWEEN 0 AND 12");
            t.HasCheckConstraint("CK_BillingPolicy_Penalty", "(\"PenaltyBasis\" = 0 AND \"PenaltyValue\" IS NULL) OR (\"PenaltyBasis\" IN (1,2) AND \"PenaltyValue\" > 0 AND \"GoverningReference\" IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.PenaltyValue).HasPrecision(18, 2);
        b.Property(x => x.PenaltyMonthCap).HasPrecision(18, 2);
        b.Property(x => x.GoverningReference).HasMaxLength(200);
        b.HasIndex(x => new { x.MunicipalityId, x.EffectiveFrom });
        b.HasOne<Municipality>().WithMany().HasForeignKey(x => x.MunicipalityId).OnDelete(DeleteBehavior.Restrict);
    }
}
