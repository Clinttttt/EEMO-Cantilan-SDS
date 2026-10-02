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

public sealed class CollectionRemittanceConfiguration : IEntityTypeConfiguration<CollectionRemittance>
{
    public void Configure(EntityTypeBuilder<CollectionRemittance> builder)
    {
        builder.ToTable("CollectionRemittances", table =>
        {
            table.HasCheckConstraint("CK_CollectionRemittances_Status", "\"Status\" IN (1, 2)");
            table.HasCheckConstraint("CK_CollectionRemittances_Amounts",
                "\"RemittedAmount\" > 0 AND \"ExpectedAmount\" >= \"RemittedAmount\" AND \"DifferenceAmount\" = \"ExpectedAmount\" - \"RemittedAmount\"");
            table.HasCheckConstraint("CK_CollectionRemittances_Period", "\"PeriodTo\" >= \"PeriodFrom\"");
            table.HasCheckConstraint("CK_CollectionRemittances_Void",
                "(\"Status\" = 1 AND \"VoidReason\" IS NULL) OR (\"Status\" = 2 AND \"VoidReason\" IS NOT NULL AND \"VoidedAtUtc\" IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.Instrument).HasConversion<int?>();
        builder.Property(x => x.ExpectedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.RemittedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.DifferenceAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.Property(x => x.Remarks).HasMaxLength(500);
        builder.Property(x => x.VoidReason).HasMaxLength(300);
        builder.Property(x => x.VoidedBy).HasMaxLength(150);
        builder.Property(x => x.IntentFingerprint).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RecordedBy).HasMaxLength(150).IsRequired();
        builder.Property(x => x.RecordedByActorId).HasMaxLength(100).IsRequired();
        // One durable operation identity per tenant: a retry can never create a second remittance.
        builder.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        builder.HasIndex(x => new { x.MunicipalityId, x.CollectorId, x.RemittanceDate });
        // Grouping only: a submission's members are found together; a collector appears once and in one position per submission.
        builder.HasIndex(x => new { x.MunicipalityId, x.SubmissionId, x.CollectorId }).IsUnique().HasFilter("\"SubmissionId\" IS NOT NULL");
        builder.HasIndex(x => new { x.MunicipalityId, x.SubmissionId, x.SubmissionSequence }).IsUnique().HasFilter("\"SubmissionId\" IS NOT NULL");

        builder.HasOne<Municipality>().WithMany()
            .HasForeignKey(x => x.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CollectionRemittanceCoverageConfiguration : IEntityTypeConfiguration<CollectionRemittanceCoverage>
{
    public void Configure(EntityTypeBuilder<CollectionRemittanceCoverage> builder)
    {
        builder.ToTable("CollectionRemittanceCoverages", table =>
            table.HasCheckConstraint("CK_CollectionRemittanceCoverages_Amount", "\"CoveredAmount\" > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CoveredAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        // Exactly once: a Collection is actively covered by at most one remittance. Voiding releases it.
        builder.HasIndex(x => new { x.MunicipalityId, x.CollectionId }).IsUnique().HasFilter("\"IsActive\"");
        builder.HasIndex(x => new { x.MunicipalityId, x.RemittanceId });

        builder.HasOne<CollectionRemittance>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.RemittanceId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Collection>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.CollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccountableFormSpoilageConfiguration : IEntityTypeConfiguration<AccountableFormSpoilage>
{
    public void Configure(EntityTypeBuilder<AccountableFormSpoilage> builder)
    {
        builder.ToTable("AccountableFormSpoilages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorName).HasMaxLength(150).IsRequired();
        // A form is spoiled at most once.
        builder.HasIndex(x => new { x.MunicipalityId, x.AccountableDocumentId }).IsUnique();
        builder.HasOne<AccountableDocument>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.AccountableDocumentId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
