using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class OfficeCashTurnoverConfiguration : IEntityTypeConfiguration<OfficeCashTurnover>
{
    public void Configure(EntityTypeBuilder<OfficeCashTurnover> builder)
    {
        builder.ToTable("OfficeCashTurnovers", table =>
        {
            table.HasCheckConstraint("CK_OfficeCashTurnovers_Status", "\"Status\" IN (1, 2)");
            table.HasCheckConstraint("CK_OfficeCashTurnovers_Amounts",
                "\"ExpectedAmount\" > 0 AND \"TurnedOverAmount\" > 0 AND \"DifferenceAmount\" = \"ExpectedAmount\" - \"TurnedOverAmount\"");
            table.HasCheckConstraint("CK_OfficeCashTurnovers_Period", "\"PeriodTo\" >= \"PeriodFrom\" AND \"TurnoverDate\" >= \"PeriodTo\"");
            table.HasCheckConstraint("CK_OfficeCashTurnovers_Evidence", "length(btrim(\"TurnedOverBy\")) > 0 AND (\"Reference\" IS NULL OR length(btrim(\"Reference\")) > 0)");
            table.HasCheckConstraint("CK_OfficeCashTurnovers_Explained", "\"DifferenceAmount\" = 0 OR \"Remarks\" IS NOT NULL");
            table.HasCheckConstraint("CK_OfficeCashTurnovers_Void",
                "(\"Status\" = 1 AND \"VoidReason\" IS NULL AND \"VoidedAtUtc\" IS NULL AND \"VoidedByActorId\" IS NULL) "
                + "OR (\"Status\" = 2 AND \"VoidReason\" IS NOT NULL AND \"VoidedAtUtc\" IS NOT NULL AND \"VoidedByActorId\" IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.ExpectedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TurnedOverAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.DifferenceAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.Property(x => x.ReferenceYear).IsRequired();
        builder.Property(x => x.ReferenceNumber).UseSequence("OfficeCashTurnoverReferenceNumberSeq").IsRequired();
        builder.Property(x => x.ReferenceCode).HasMaxLength(40)
            .HasComputedColumnSql("'OCT-' || \"ReferenceYear\"::text || '-' || repeat('0', greatest(6 - length(\"ReferenceNumber\"::text), 0)) || \"ReferenceNumber\"::text", stored: true);
        builder.HasIndex(x => new { x.MunicipalityId, x.ReferenceYear, x.ReferenceNumber }).IsUnique();
        builder.Property(x => x.TurnedOverBy).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Remarks).HasMaxLength(500);
        builder.Property(x => x.VoidReason).HasMaxLength(300);
        builder.Property(x => x.VoidedBy).HasMaxLength(150);
        builder.Property(x => x.VoidedByActorId).HasMaxLength(100);
        builder.Property(x => x.IntentFingerprint).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RecordedBy).HasMaxLength(150).IsRequired();
        builder.Property(x => x.RecordedByActorId).HasMaxLength(100).IsRequired();
        // One durable operation identity per tenant: a retry can never create a second turnover.
        builder.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        builder.HasIndex(x => new { x.MunicipalityId, x.TurnoverDate });

        builder.HasOne<Municipality>().WithMany()
            .HasForeignKey(x => x.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OfficeCashTurnoverCoverageConfiguration : IEntityTypeConfiguration<OfficeCashTurnoverCoverage>
{
    public void Configure(EntityTypeBuilder<OfficeCashTurnoverCoverage> builder)
    {
        builder.ToTable("OfficeCashTurnoverCoverages", table =>
            table.HasCheckConstraint("CK_OfficeCashTurnoverCoverages_Amount", "\"CoveredAmount\" > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CoveredAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.OriginalActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.OriginalActorName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        // Exactly once: a Collection is actively covered by at most one turnover. Voiding releases it.
        builder.HasIndex(x => new { x.MunicipalityId, x.CollectionId }).IsUnique().HasFilter("\"IsActive\"");
        builder.HasIndex(x => new { x.MunicipalityId, x.TurnoverId });

        builder.HasOne<OfficeCashTurnover>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.TurnoverId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Collection>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.CollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
