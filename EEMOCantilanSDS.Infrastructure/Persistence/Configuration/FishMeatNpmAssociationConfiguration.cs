using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
public sealed class FishMeatNpmAssociationConfiguration : IEntityTypeConfiguration<FishMeatNpmAssociation>
{
    public void Configure(EntityTypeBuilder<FishMeatNpmAssociation> b)
    {
        b.ToTable("FishMeatNpmAssociations", t => t.HasCheckConstraint("CK_NpmAssociation_Correction",
            "(\"CorrectedAtUtc\" IS NULL AND \"CorrectionOperationId\" IS NULL AND \"CorrectedBy\" IS NULL AND \"CorrectionReason\" IS NULL) OR (\"CorrectedAtUtc\" IS NOT NULL AND \"CorrectionOperationId\" IS NOT NULL AND \"CorrectedBy\" IS NOT NULL AND \"CorrectionReason\" IS NOT NULL)"));
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        b.HasIndex(x => new { x.MunicipalityId, x.CorrectionOperationId }).IsUnique().HasFilter("\"CorrectionOperationId\" IS NOT NULL");
        b.HasIndex(x => new { x.MunicipalityId, x.VendorRegistrationId, x.ContractId }).IsUnique().HasFilter("\"CorrectedAtUtc\" IS NULL");
        b.HasOne<FishMeatVendorRegistration>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.VendorRegistrationId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Contract>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ContractId, x.StallId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id, x.StallId }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.RequestFingerprint).HasMaxLength(64);
        b.Property(x => x.SourceVersion).HasMaxLength(64);
        b.Property(x => x.OccupantSnapshot).HasMaxLength(100);
        b.Property(x => x.ConfirmationNote).HasMaxLength(500);
        b.Property(x => x.CorrectionReason).HasMaxLength(500);
        b.Property(x => x.ConfirmedBy).HasMaxLength(100);
        b.Property(x => x.CorrectedBy).HasMaxLength(100);
    }
}
