using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class MunicipalityConfiguration : IEntityTypeConfiguration<Municipality>
    {
        public void Configure(EntityTypeBuilder<Municipality> builder)
        {
            builder.ToTable("Municipalities");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Code)
                .IsRequired()
                .HasMaxLength(30);

            builder.Property(x => x.TenantCode)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(120);

            builder.Property(x => x.Province)
                .IsRequired()
                .HasMaxLength(120);

            builder.Property(x => x.Address)
                .HasMaxLength(300);

            builder.Property(x => x.SealPath)
                .HasMaxLength(300);

            builder.Property(x => x.OfficeName)
                .HasMaxLength(160);

            builder.Property(x => x.Status)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(x => x.IsDefault)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(x => x.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.HasIndex(x => x.Code).IsUnique();
            builder.HasIndex(x => x.TenantCode).IsUnique();

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
