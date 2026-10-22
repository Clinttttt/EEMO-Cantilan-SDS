using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
public sealed class FishMeatVendorRegistrationConfiguration : IEntityTypeConfiguration<FishMeatVendorRegistration>
{
    public void Configure(EntityTypeBuilder<FishMeatVendorRegistration> b)
    {
        b.ToTable("FishMeatVendorRegistrations", t => {
            t.HasCheckConstraint("CK_VendorRegistration_Type", "\"VendorType\" IN (1,2) AND \"RegistrationKind\" IN (1,2)");
            t.HasCheckConstraint("CK_VendorRegistration_Year", "\"TaxYear\" BETWEEN 2000 AND 2200");
            t.HasCheckConstraint("CK_VendorRegistration_Lifecycle", "(\"Status\" = 1 AND \"CloseClientOperationId\" IS NULL AND \"ClosedOn\" IS NULL AND \"ClosedAtUtc\" IS NULL AND \"ClosedBy\" IS NULL AND \"CloseNote\" IS NULL) OR (\"Status\" IN (2,3) AND \"CloseClientOperationId\" IS NOT NULL AND \"ClosedOn\" IS NOT NULL AND \"ClosedAtUtc\" IS NOT NULL AND \"ClosedBy\" IS NOT NULL)");
            t.HasCheckConstraint("CK_VendorRegistration_Deletion", "(\"Status\" IN (1,2) AND \"DeleteClientOperationId\" IS NULL AND \"DeletedAtUtc\" IS NULL AND \"DeletedBy\" IS NULL AND \"DeleteReason\" IS NULL) OR (\"Status\" = 3 AND \"DeleteClientOperationId\" IS NOT NULL AND \"DeletedAtUtc\" IS NOT NULL AND \"DeletedBy\" IS NOT NULL AND \"DeleteReason\" IS NOT NULL AND length(btrim(\"DeletedBy\")) > 0 AND length(btrim(\"DeleteReason\")) > 0)");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        b.Property(x => x.Status).HasDefaultValue(VendorRegistrationStatus.Active);
        b.Property(x => x.ClosedBy).HasMaxLength(100);
        b.Property(x => x.CloseNote).HasMaxLength(300);
        b.Property(x => x.Status).IsConcurrencyToken();
        b.Property(x => x.DeletedBy).HasMaxLength(100);
        b.Property(x => x.DeleteReason).HasMaxLength(500);
        b.HasIndex(x => new { x.MunicipalityId, x.DeleteClientOperationId }).IsUnique().HasFilter("\"DeleteClientOperationId\" IS NOT NULL");
        b.HasIndex(x => new { x.MunicipalityId, x.CloseClientOperationId }).IsUnique().HasFilter("\"CloseClientOperationId\" IS NOT NULL");
        b.HasIndex(x => new { x.MunicipalityId, x.PriorRegistrationId, x.TaxYear }).IsUnique().HasFilter("\"PriorRegistrationId\" IS NOT NULL");
        b.HasOne<FishMeatVendorRegistration>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.PriorRegistrationId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.DisplayName).HasMaxLength(200);
        b.Property(x => x.BusinessName).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.LastName).HasMaxLength(100);
        b.Property(x => x.FirstName).HasMaxLength(100);
        b.Property(x => x.MiddleName).HasMaxLength(100);
        b.Property(x => x.NameExtension).HasMaxLength(30);
        b.Property(x => x.Sex).HasMaxLength(30);
        b.Property(x => x.NatureOfBusiness).HasMaxLength(300);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.HasOne<Municipality>().WithMany().HasForeignKey(x => x.MunicipalityId).OnDelete(DeleteBehavior.Restrict);
    }
}
