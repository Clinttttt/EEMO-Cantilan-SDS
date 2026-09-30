using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class ObligationAccountConfiguration : IEntityTypeConfiguration<ObligationAccount>
{
    public void Configure(EntityTypeBuilder<ObligationAccount> builder)
    {
        builder.ToTable("ObligationAccounts", table =>
        {
            table.HasCheckConstraint("CK_ObligationAccounts_Kind", "\"Kind\" IN (1, 2, 3)");
            // A vendor fee is anchored to an NPM stall; a lot rental carries its event; nothing else carries either.
            table.HasCheckConstraint(
                "CK_ObligationAccounts_Shape",
                "((\"Kind\" = 1 AND \"StallId\" IS NOT NULL AND \"Event\" IS NULL AND \"EventDate\" IS NULL) " +
                "OR (\"Kind\" = 2 AND \"StallId\" IS NULL AND \"Event\" IS NULL AND \"EventDate\" IS NULL) " +
                "OR (\"Kind\" = 3 AND \"StallId\" IS NULL AND \"Event\" IN (1, 2) AND \"EventDate\" IS NOT NULL))");
            table.HasCheckConstraint("CK_ObligationAccounts_Window", "\"ActiveTo\" IS NULL OR \"ActiveTo\" >= \"ActiveFrom\"");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.Kind).HasConversion<int>().IsRequired();
        builder.Property(x => x.Event).HasConversion<int?>();
        builder.Property(x => x.SubjectLabel).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ActiveFrom).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.Kind, x.PayorId });
        // One vendor-fee account per NPM stall: the vendor context is not a second registry.
        builder.HasIndex(x => new { x.MunicipalityId, x.StallId }).IsUnique()
            .HasFilter("\"Kind\" = 1 AND \"ActiveTo\" IS NULL");

        builder.HasOne<Municipality>().WithMany()
            .HasForeignKey(x => x.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payor>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.PayorId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ObligationRateConfiguration : IEntityTypeConfiguration<ObligationRate>
{
    public void Configure(EntityTypeBuilder<ObligationRate> builder)
    {
        builder.ToTable("ObligationRates", table =>
            table.HasCheckConstraint("CK_ObligationRates_Amount", "\"Amount\" > 0"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.EffectiveFrom).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.ObligationAccountId, x.EffectiveFrom });

        builder.HasOne<ObligationAccount>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.ObligationAccountId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ObligationPeriodConfiguration : IEntityTypeConfiguration<ObligationPeriod>
{
    public void Configure(EntityTypeBuilder<ObligationPeriod> builder)
    {
        builder.ToTable("ObligationPeriods", table =>
            table.HasCheckConstraint("CK_ObligationPeriods_Assessed", "\"AssessedAmount\" > 0"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.AssessedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.PeriodStart).IsRequired();
        builder.Property(x => x.SettlementVersion).HasDefaultValue(1L).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        // A period is assessed once, so it can carry one obligation and one balance.
        builder.HasIndex(x => new { x.MunicipalityId, x.ObligationAccountId, x.PeriodStart }).IsUnique();

        builder.HasOne<ObligationAccount>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.ObligationAccountId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ObligationRate>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.ObligationRateId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
