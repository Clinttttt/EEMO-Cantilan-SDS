using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
public sealed class DirectFineAuthorityConfiguration : IEntityTypeConfiguration<DirectFineAuthority>
{
    public void Configure(EntityTypeBuilder<DirectFineAuthority> b)
    {
        b.ToTable("DirectFineAuthorities", t => t.HasCheckConstraint("CK_DirectFineAuthority_Dates", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\""));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        b.Property(x => x.GoverningReference).HasMaxLength(200).IsRequired();
        b.Property(x => x.ApprovalReference).HasMaxLength(200).IsRequired();
        b.HasOne<PenaltyDefinition>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.PenaltyVersionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Municipality>().WithMany().HasForeignKey(x => x.MunicipalityId).OnDelete(DeleteBehavior.Restrict);
    }
}
