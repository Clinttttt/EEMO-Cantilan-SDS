using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

/// <summary>
/// Typed financial mapping. Installing its additive migration does not authorize a tenant's settlement runtime.
/// </summary>
public static class NpmRentMonthFinancialSchema
{
    public static void Configure(ModelBuilder model)
    {
        var allocation = model.Entity<CollectionAllocation>();
        allocation.Property(x => x.NpmRentMonthId);
        allocation.ToTable("CollectionAllocations", t =>
        {
            t.HasCheckConstraint("CK_CollectionAllocations_SourceShape",
                "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 10, 15, 17) AND \"SourcePart\" IS NULL))");
            t.HasCheckConstraint("CK_CollectionAllocations_NpmMonthIdentity",
                "(\"SourceKind\" = 17 AND \"NpmRentMonthId\" IS NOT NULL AND \"NpmRentMonthId\" = \"SourceId\") OR (\"SourceKind\" <> 17 AND \"NpmRentMonthId\" IS NULL)");
        });
        allocation.HasOne<NpmRentMonth>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.NpmRentMonthId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CollectionLine>().ToTable("CollectionLines", t => t.HasCheckConstraint("CK_CollectionLines_SourceShape",
            "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))"));
        var month = model.Entity<NpmRentMonth>();
        month.Property(x => x.SettlementSealedAtUtc);
        month.Property(x => x.SettlementActivatedAtUtc);
        month.Property(x => x.SettlementActivatedByUserId);
        month.ToTable("NpmRentMonths", t =>
        {
            t.HasCheckConstraint("CK_NpmRentMonths_Review",
                "(\"Status\" = 1 AND \"ReconciledByUserId\" IS NULL AND \"ReconciledAtUtc\" IS NULL AND \"ReconciliationEvidence\" IS NULL) OR (\"Status\" IN (2,3) AND \"MonthBasis\" IS NOT NULL AND \"ReconciledByUserId\" IS NOT NULL AND \"ReconciledByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ReconciledAtUtc\" IS NOT NULL AND \"ReconciledAtUtc\" >= \"CreatedAtUtc\" AND \"ReconciliationEvidence\" IS NOT NULL AND length(btrim(\"ReconciliationEvidence\")) > 0)");
            t.HasCheckConstraint("CK_NpmRentMonths_Activation",
                "(\"Status\" IN (1,2) AND \"SettlementActivatedAtUtc\" IS NULL AND \"SettlementActivatedByUserId\" IS NULL) OR (\"Status\" = 3 AND \"SettlementActivatedAtUtc\" IS NOT NULL AND \"SettlementActivatedAtUtc\" >= \"ReconciledAtUtc\" AND \"SettlementActivatedByUserId\" IS NOT NULL AND \"SettlementActivatedByUserId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
        });
        month.ToTable("NpmRentMonths", t => t.HasCheckConstraint("CK_NpmRentMonths_Sealed",
            "\"SettlementSealedAtUtc\" IS NULL OR (\"Status\" = 3 AND \"SettlementSealedAtUtc\" >= \"SettlementActivatedAtUtc\" AND \"SettlementActivatedAtUtc\" IS NOT NULL)"));
    }
}
