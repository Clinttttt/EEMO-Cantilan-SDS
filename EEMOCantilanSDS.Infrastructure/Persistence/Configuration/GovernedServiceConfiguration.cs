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
            table.HasCheckConstraint("CK_GovernedServiceSettings_Basis", "\"Basis\" IN (1, 2, 3)");
            table.HasCheckConstraint(
                "CK_GovernedServiceSettings_AmountShape",
                "((\"Basis\" = 1 AND \"FixedAmount\" IS NOT NULL AND \"FixedAmount\" > 0 AND \"MaximumAmount\" IS NULL) " +
                "OR (\"Basis\" = 2 AND \"FixedAmount\" IS NULL AND (\"MaximumAmount\" IS NULL OR \"MaximumAmount\" > 0)) " +
                "OR (\"Basis\" = 3 AND \"FixedAmount\" IS NULL AND \"MaximumAmount\" IS NULL))");
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

public sealed class PenaltyDefinitionConfiguration : IEntityTypeConfiguration<PenaltyDefinition>
{
    public void Configure(EntityTypeBuilder<PenaltyDefinition> builder)
    {
        builder.ToTable("PenaltyDefinitions", table =>
        {
            table.HasCheckConstraint("CK_PenaltyDefinitions_Code", "\"Code\" ~ '^[A-Z][A-Z0-9_]{1,39}$'");
            table.HasCheckConstraint("CK_PenaltyDefinitions_Basis", "\"Basis\" IN (1, 2)");
            table.HasCheckConstraint(
                "CK_PenaltyDefinitions_AmountShape",
                "((\"Basis\" = 1 AND \"FixedAmount\" IS NOT NULL AND \"FixedAmount\" > 0 AND \"MaximumAmount\" IS NULL) " +
                "OR (\"Basis\" = 2 AND \"FixedAmount\" IS NULL AND (\"MaximumAmount\" IS NULL OR \"MaximumAmount\" > 0)))");
        });
        builder.HasKey(x => x.Id);
        // A collection line's SourceId is a version id, so tenant-consistent reads and lookups go through this pair.
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.AppliesTo).HasMaxLength(80);
        builder.Property(x => x.Basis).HasConversion<int>().IsRequired();
        builder.Property(x => x.FixedAmount).HasPrecision(18, 2);
        builder.Property(x => x.MaximumAmount).HasPrecision(18, 2);
        builder.Property(x => x.EffectiveDate).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.Code, x.EffectiveDate });

        builder.HasOne<Municipality>().WithMany()
            .HasForeignKey(x => x.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VehicleClassConfiguration : IEntityTypeConfiguration<VehicleClass>
{
    public void Configure(EntityTypeBuilder<VehicleClass> builder)
    {
        builder.ToTable("VehicleClasses", table =>
            table.HasCheckConstraint("CK_VehicleClasses_Code", "\"Code\" ~ '^[A-Z][A-Z0-9_]{1,39}$'"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.Code }).IsUnique();

        builder.HasOne<Municipality>().WithMany()
            .HasForeignKey(x => x.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VehicleClassRateConfiguration : IEntityTypeConfiguration<VehicleClassRate>
{
    public void Configure(EntityTypeBuilder<VehicleClassRate> builder)
    {
        builder.ToTable("VehicleClassRates", table =>
            table.HasCheckConstraint("CK_VehicleClassRates_Amount", "\"Amount\" > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.EffectiveDate).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.VehicleClassId, x.EffectiveDate });

        builder.HasOne<VehicleClass>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.VehicleClassId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
