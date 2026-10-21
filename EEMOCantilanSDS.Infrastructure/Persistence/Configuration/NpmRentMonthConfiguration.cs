using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

/// <summary>Preparation model only. No collection source-kind or money-routing changes are included.</summary>
public sealed class NpmRentMonthConfiguration : IEntityTypeConfiguration<NpmRentMonth>
{
    public void Configure(EntityTypeBuilder<NpmRentMonth> b)
    {
        b.ToTable("NpmRentMonths", t =>
        {
            t.HasCheckConstraint("CK_NpmRentMonths_Period", "EXTRACT(DAY FROM \"PeriodStart\") = 1");
            t.HasCheckConstraint("CK_NpmRentMonths_Money", "\"AssessedAmount\" >= 0 AND \"CreditedAmount\" >= 0 AND \"CreditedAmount\" <= \"AssessedAmount\"");
            t.HasCheckConstraint("CK_NpmRentMonths_Basis", "\"MonthBasis\" IS NULL OR \"MonthBasis\" IN (1, 2)");
            t.HasCheckConstraint("CK_NpmRentMonths_Version", "\"SettlementVersion\" > 0");
            t.HasCheckConstraint("CK_NpmRentMonths_RevisionReason", "(\"UpdatedAtUtc\" IS NULL AND \"LastRevisionReason\" IS NULL) OR (\"UpdatedAtUtc\" IS NOT NULL AND \"LastRevisionReason\" IS NOT NULL AND length(btrim(\"LastRevisionReason\")) > 0)");
            t.HasCheckConstraint("CK_NpmRentMonths_Identity", "\"MunicipalityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"FacilityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"StallId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ContractId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CreatedByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            t.HasCheckConstraint("CK_NpmRentMonths_Evidence", "length(btrim(\"PolicyReference\")) > 0 AND length(btrim(\"CreatedBy\")) > 0 AND jsonb_typeof(\"RateEvidenceJson\") = 'object' AND \"RateEvidenceJson\" <> '{}'::jsonb AND jsonb_typeof(\"CreditEvidenceJson\") = 'object' AND \"CreditEvidenceJson\" <> '{}'::jsonb");
            t.HasCheckConstraint("CK_NpmRentMonths_Review", "(\"Status\" = 1 AND \"ReconciledByUserId\" IS NULL AND \"ReconciledAtUtc\" IS NULL AND \"ReconciliationEvidence\" IS NULL) OR (\"Status\" = 2 AND \"MonthBasis\" IS NOT NULL AND \"ReconciledByUserId\" IS NOT NULL AND \"ReconciledByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ReconciledAtUtc\" IS NOT NULL AND \"ReconciledAtUtc\" >= \"CreatedAtUtc\" AND \"ReconciliationEvidence\" IS NOT NULL AND length(btrim(\"ReconciliationEvidence\")) > 0)");
            t.HasCheckConstraint("CK_NpmRentMonths_Updated", "(\"UpdatedAtUtc\" IS NULL AND \"UpdatedByUserId\" IS NULL AND \"UpdatedBy\" IS NULL) OR (\"UpdatedAtUtc\" IS NOT NULL AND \"UpdatedAtUtc\" >= \"CreatedAtUtc\" AND \"UpdatedByUserId\" IS NOT NULL AND \"UpdatedByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"UpdatedBy\" IS NOT NULL AND length(btrim(\"UpdatedBy\")) > 0)");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        b.HasIndex(x => new { x.MunicipalityId, x.StallId, x.PeriodStart }).IsUnique();
        b.HasOne<Facility>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.FacilityId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Stall>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.StallId, x.FacilityId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id, x.FacilityId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Contract>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ContractId, x.StallId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id, x.StallId }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.AssessedAmount).HasPrecision(18, 2);
        b.Property(x => x.CreditedAmount).HasPrecision(18, 2);
        b.Property(x => x.MonthBasis).HasConversion<int?>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.SettlementVersion).IsConcurrencyToken();
        b.Property(x => x.PolicyReference).HasMaxLength(1000).IsRequired();
        b.Property(x => x.RateEvidenceJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CreditEvidenceJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.Property(x => x.LastRevisionReason).HasMaxLength(4000);
        b.Property(x => x.ReconciliationEvidence).HasMaxLength(4000);
        b.Ignore(x => x.CanAcceptCollections);
        b.Ignore(x => x.SettlementSealedAtUtc); // Not installed by the approved preparation-only foundation.
        b.Ignore(x => x.SettlementActivatedAtUtc);
        b.Ignore(x => x.SettlementActivatedByUserId);
    }
}
