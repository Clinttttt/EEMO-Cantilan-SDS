using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class SlaughterAnimalRateConfiguration : IEntityTypeConfiguration<SlaughterAnimalRate>
    {
        public void Configure(EntityTypeBuilder<SlaughterAnimalRate> builder)
        {
            builder.ToTable("SlaughterAnimalRates");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.MunicipalityId).IsRequired();
            builder.Property(x => x.AnimalName).IsRequired().HasMaxLength(100);
            builder.Property(x => x.RatePerHead).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

            // One custom animal name per municipality.
            builder.HasIndex(x => new { x.MunicipalityId, x.AnimalName }).IsUnique();

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
