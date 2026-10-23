using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class WeighingRateVersionConfiguration : IEntityTypeConfiguration<WeighingRateVersion>
{
    public void Configure(EntityTypeBuilder<WeighingRateVersion> builder)
    {
        builder.ToTable("WeighingRateVersions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.VendorType).HasConversion<int>().IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired();
        builder.Property(x => x.RatePerKilo).HasPrecision(18, 2);
        builder.Property(x => x.EffectiveDate).IsRequired();
        builder.Property(x => x.RecordedAtUtc).IsRequired();
        builder.Property(x => x.RecordedBy).HasMaxLength(100).IsRequired();
        // Multiple revisions of tomorrow's schedule on the same day are retained; last recorded wins.
        builder.HasIndex(x => new { x.MunicipalityId, x.VendorType, x.EffectiveDate, x.RecordedAtUtc });
        builder.ToTable(t => t.HasCheckConstraint("CK_WeighingRateVersions_Amount",
            "(\"IsEnabled\" = TRUE AND \"RatePerKilo\" > 0) OR (\"IsEnabled\" = FALSE AND \"RatePerKilo\" IS NULL)"));
    }
}
