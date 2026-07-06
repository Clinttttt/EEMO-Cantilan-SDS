using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class OrSeriesConfigConfiguration : IEntityTypeConfiguration<OrSeriesConfig>
    {
        public void Configure(EntityTypeBuilder<OrSeriesConfig> builder)
        {
            builder.ToTable("OrSeriesConfigs");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.MunicipalityId).IsRequired();
            builder.Property(x => x.Prefix).HasMaxLength(30);
            builder.Property(x => x.NextNumber).IsRequired();
            builder.Property(x => x.PadWidth).IsRequired();
            builder.Property(x => x.IsEnabled).IsRequired().HasDefaultValue(true);

            // One OR-series config per municipality.
            builder.HasIndex(x => x.MunicipalityId).IsUnique();

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
