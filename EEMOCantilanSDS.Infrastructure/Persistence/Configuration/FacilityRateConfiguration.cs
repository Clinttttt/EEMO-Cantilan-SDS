using EEMOCantilanSDS.Domain.Entities.Facilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class FacilityRateConfiguration : IEntityTypeConfiguration<FacilityRate>
    {
        public void Configure(EntityTypeBuilder<FacilityRate> builder)
        {
            builder.ToTable("FacilityRates");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.MunicipalityId).IsRequired();
            builder.Property(x => x.FacilityCode).IsRequired().HasConversion<int>();
            builder.Property(x => x.RateKey).IsRequired().HasConversion<int>();
            builder.Property(x => x.Amount).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.EffectiveDate).IsRequired();

            // One rate row per municipality + facility + rate key + effective date.
            builder.HasIndex(x => new { x.MunicipalityId, x.FacilityCode, x.RateKey, x.EffectiveDate })
                .IsUnique();

            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.CreatedBy).HasMaxLength(100);
            builder.Property(x => x.UpdatedAt);
            builder.Property(x => x.UpdatedBy).HasMaxLength(100);
            builder.Property(x => x.IsDeleted).HasDefaultValue(false);
            builder.Property(x => x.DeletedAt);
            builder.Property(x => x.DeletedBy).HasMaxLength(100);
        }
    }
}
