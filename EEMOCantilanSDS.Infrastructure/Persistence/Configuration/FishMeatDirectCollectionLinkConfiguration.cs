using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

/// <summary>Append-only link from a posted direct Fish/Meat Collection to a registered vendor. Restrict-delete on both sides; one link per Collection.</summary>
public sealed class FishMeatDirectCollectionLinkConfiguration : IEntityTypeConfiguration<FishMeatDirectCollectionLink>
{
    public void Configure(EntityTypeBuilder<FishMeatDirectCollectionLink> b)
    {
        b.ToTable("FishMeatDirectCollectionLinks");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        b.HasIndex(x => new { x.MunicipalityId, x.CollectionId }).IsUnique();
        b.HasIndex(x => new { x.MunicipalityId, x.VendorRegistrationId });
        b.HasOne<Collection>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.CollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FishMeatVendorRegistration>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.VendorRegistrationId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.ActorName).HasMaxLength(100);
    }
}
