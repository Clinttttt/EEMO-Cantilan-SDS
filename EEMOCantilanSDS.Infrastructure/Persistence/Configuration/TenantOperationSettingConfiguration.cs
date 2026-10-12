using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class TenantOperationSettingConfiguration : IEntityTypeConfiguration<TenantOperationSetting>
    {
        public void Configure(EntityTypeBuilder<TenantOperationSetting> builder)
        {
            builder.ToTable("TenantOperationSettings");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.MunicipalityId).IsRequired();
            builder.Property(x => x.OperationCode).IsRequired().HasMaxLength(64);
            builder.Property(x => x.IsListed).IsRequired();
            builder.Property(x => x.LocalDisplayName).HasMaxLength(TenantOperationCatalog.MaxLabelLength);
            builder.Property(x => x.Origin).IsRequired().HasMaxLength(20);
            // Optimistic concurrency: a second Head saving over a changed row is refused rather than overwriting it.
            builder.Property(x => x.Version).IsRequired().HasDefaultValue(1L).IsConcurrencyToken();

            // One setting per municipality and operation. Soft-deleted rows are not used (settings are toggled, never deleted),
            // so a plain unique index is the whole rule.
            builder.HasIndex(x => new { x.MunicipalityId, x.OperationCode }).IsUnique();

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
