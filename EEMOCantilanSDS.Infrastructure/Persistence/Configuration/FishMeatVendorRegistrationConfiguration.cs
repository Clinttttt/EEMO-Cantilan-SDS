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
        });
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        b.Property(x => x.DisplayName).HasMaxLength(200);
        b.Property(x => x.BusinessName).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.HasOne<Municipality>().WithMany().HasForeignKey(x => x.MunicipalityId).OnDelete(DeleteBehavior.Restrict);
    }
}
