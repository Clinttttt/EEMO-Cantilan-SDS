using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
public sealed class NpmRegistrationRequestConfiguration : IEntityTypeConfiguration<NpmRegistrationRequest>
{
    public void Configure(EntityTypeBuilder<NpmRegistrationRequest> b)
    {
        b.ToTable("NpmRegistrationRequests", t => {
            t.HasCheckConstraint("CK_NpmRegistrationRequest_State", "\"Status\" IN (1,2,3,4,5) AND \"Version\" > 0 AND ((\"Status\" = 1 AND \"DecisionOperationId\" IS NULL AND \"ReviewedAtUtc\" IS NULL AND \"AssociationId\" IS NULL) OR (\"Status\" <> 1 AND \"DecisionOperationId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewedBy\" IS NOT NULL AND \"ReviewReason\" IS NOT NULL)) AND ((\"Status\" = 2 AND \"AssociationId\" IS NOT NULL) OR (\"Status\" <> 2 AND \"AssociationId\" IS NULL))");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        b.HasIndex(x => new { x.MunicipalityId, x.DecisionOperationId }).IsUnique().HasFilter("\"DecisionOperationId\" IS NOT NULL");
        b.HasIndex(x => new { x.MunicipalityId, x.CollectorId, x.VendorRegistrationId }).IsUnique().HasFilter("\"Status\" = 1");
        b.HasIndex(x => new { x.MunicipalityId, x.SubmittedAtUtc, x.Id });
        b.HasOne<FishMeatVendorRegistration>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.VendorRegistrationId }).HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BaseUser>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.CollectorId }).HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FishMeatNpmAssociation>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.AssociationId }).HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<NpmRegistrationRequest>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ReplacesRequestId }).HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.ProposedStall).HasMaxLength(20);
        b.Property(x => x.VendorName).HasMaxLength(200); b.Property(x => x.CollectorName).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(1000); b.Property(x => x.ReviewReason).HasMaxLength(1000); b.Property(x => x.ReviewedBy).HasMaxLength(100);
    }
}
