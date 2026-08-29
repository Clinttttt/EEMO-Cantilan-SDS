using EEMOCantilanSDS.Domain.Entities.Facilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class FacilitySectionUtilitiesConfiguration : IEntityTypeConfiguration<FacilitySectionUtilities>
    {
        public void Configure(EntityTypeBuilder<FacilitySectionUtilities> builder)
        {
            builder.ToTable("FacilitySectionUtilities");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.MunicipalityId).IsRequired();
            builder.Property(x => x.FacilityCode).IsRequired().HasConversion<int>();
            builder.Property(x => x.SectionName).IsRequired().HasMaxLength(60);
            builder.Property(x => x.Electricity).IsRequired().HasDefaultValue(false);
            builder.Property(x => x.Water).IsRequired().HasDefaultValue(false);

            // One row per municipality + facility + section: this is the section's current default, not a history.
            builder.HasIndex(x => new { x.MunicipalityId, x.FacilityCode, x.SectionName })
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
