using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class FishMeatRegistrationRequestConfiguration : IEntityTypeConfiguration<FishMeatRegistrationRequest>
{
    public void Configure(EntityTypeBuilder<FishMeatRegistrationRequest> b)
    {
        b.ToTable("FishMeatRegistrationRequests", t => {
            t.HasCheckConstraint("CK_FishMeatRequest_ReviewEvidence", "(\"Status\" = 1 AND \"ReviewedByUserId\" IS NULL) OR (\"Status\" <> 1 AND \"ReviewedByUserId\" IS NOT NULL AND \"Reason\" IS NOT NULL)");
            t.HasCheckConstraint("CK_FishMeatRequest_Identity", "\"TaxYear\" BETWEEN 2000 AND 2200 AND \"VendorType\" IN (1,2) AND length(btrim(\"VendorName\")) > 0");
            t.HasCheckConstraint("CK_FishMeatRequest_State", "\"Status\" IN (1,2,3,4,5) AND ((\"Status\" = 1 AND \"Version\" = 1 AND \"DecisionOperationId\" IS NULL AND \"DecisionFingerprint\" IS NULL AND \"ReviewedAtUtc\" IS NULL AND \"ReviewedBy\" IS NULL AND \"Reason\" IS NULL) OR (\"Status\" <> 1 AND \"Version\" = 2 AND \"DecisionOperationId\" IS NOT NULL AND \"DecisionFingerprint\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewedBy\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)) AND ((\"Status\" = 2 AND \"RegistrationId\" IS NOT NULL) OR (\"Status\" <> 2 AND \"RegistrationId\" IS NULL))");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        b.HasIndex(x => new { x.MunicipalityId, x.DecisionOperationId }).IsUnique().HasFilter("\"DecisionOperationId\" IS NOT NULL");
        // A submission duplicate guard, never a vendor identity or a cross-collector name match.
        b.HasIndex(x => new { x.MunicipalityId, x.CollectorId, x.PendingKey }).IsUnique().HasFilter("\"Status\" = 1");
        b.HasIndex(x => new { x.MunicipalityId, x.Status, x.SubmittedAtUtc, x.Id });
        b.HasIndex(x => new { x.MunicipalityId, x.ReplacesRequestId }).IsUnique().HasFilter("\"ReplacesRequestId\" IS NOT NULL");
        b.HasOne<BaseUser>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.CollectorId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BaseUser>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ReviewedByUserId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FishMeatVendorRegistration>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.RegistrationId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FishMeatRegistrationRequest>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ReplacesRequestId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.PendingKey).HasMaxLength(64);
        b.Property(x => x.DecisionFingerprint).HasMaxLength(64);
        b.Property(x => x.VendorName).HasMaxLength(200); b.Property(x => x.Contact).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(1000); b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.SubmittedBy).HasMaxLength(100); b.Property(x => x.ReviewedBy).HasMaxLength(100);
    }
}
