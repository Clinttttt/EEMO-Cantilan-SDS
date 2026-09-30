using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class GovernedServiceConfiguration : IEntityTypeConfiguration<GovernedService>
{
    public void Configure(EntityTypeBuilder<GovernedService> builder)
    {
        builder.ToTable("GovernedServices", table =>
            table.HasCheckConstraint("CK_GovernedServices_OperationCode",
                "\"OperationCode\" ~ '^[A-Z0-9_]{1,64}$'"));
        builder.HasKey(x => x.Id);
        // Tenant-consistent foreign keys from settings and evidence target this pair.
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.OperationCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.OperationCode }).IsUnique();

        builder.HasOne<Municipality>().WithMany()
            .HasForeignKey(x => x.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GovernedServiceSettingConfiguration : IEntityTypeConfiguration<GovernedServiceSetting>
{
    public void Configure(EntityTypeBuilder<GovernedServiceSetting> builder)
    {
        builder.ToTable("GovernedServiceSettings", table =>
        {
            table.HasCheckConstraint("CK_GovernedServiceSettings_Basis", "\"Basis\" IN (1, 2)");
            table.HasCheckConstraint(
                "CK_GovernedServiceSettings_AmountShape",
                "((\"Basis\" = 1 AND \"FixedAmount\" IS NOT NULL AND \"FixedAmount\" > 0 AND \"MaximumAmount\" IS NULL) " +
                "OR (\"Basis\" = 2 AND \"FixedAmount\" IS NULL AND (\"MaximumAmount\" IS NULL OR \"MaximumAmount\" > 0)))");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Basis).HasConversion<int>().IsRequired();
        builder.Property(x => x.FixedAmount).HasPrecision(18, 2);
        builder.Property(x => x.MaximumAmount).HasPrecision(18, 2);
        builder.Property(x => x.EffectiveDate).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.GovernedServiceId, x.EffectiveDate });

        builder.HasOne<GovernedService>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.GovernedServiceId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
