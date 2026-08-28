using EEMOCantilanSDS.Domain.Entities.Facilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class FacilitySectionRateConfiguration : IEntityTypeConfiguration<FacilitySectionRate>
    {
        public void Configure(EntityTypeBuilder<FacilitySectionRate> builder)
        {
            builder.ToTable("FacilitySectionRates");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.MunicipalityId).IsRequired();
            builder.Property(x => x.FacilityCode).IsRequired().HasConversion<int>();
            // Bounded exactly as a section name is where it is registered, so the two cannot disagree about what fits.
            builder.Property(x => x.SectionName).IsRequired().HasMaxLength(60);
            builder.Property(x => x.Amount).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.EffectiveDate).IsRequired();

            // One row per municipality + facility + section + effective date, mirroring FacilityRates. The section name
            // carries the LGU's own casing; matching is case-insensitive in the resolver, and the write path trims and
            // reuses the registered name, so two rows cannot describe one section under different spellings.
            builder.HasIndex(x => new { x.MunicipalityId, x.FacilityCode, x.SectionName, x.EffectiveDate })
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
