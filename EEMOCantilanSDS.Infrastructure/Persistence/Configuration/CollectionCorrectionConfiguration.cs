using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class CollectionCorrectionConfiguration : IEntityTypeConfiguration<CollectionCorrection>
{
    public void Configure(EntityTypeBuilder<CollectionCorrection> builder)
    {
        builder.ToTable("CollectionCorrections");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.OriginalCollectionId).IsRequired();
        builder.Property(x => x.OriginalDocumentId);
        builder.Property(x => x.ReplacementDocumentId);
        builder.Property(x => x.ReplacementCollectionId);
        builder.Property(x => x.CorrectionType).HasConversion<int>().IsRequired();
        builder.Property(x => x.CorrectionEffectiveDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.RecordedAtUtc).IsRequired();
        builder.Property(x => x.FinancialEffectAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorName).HasMaxLength(150).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.OriginalCollectionId, x.RecordedAtUtc });
        builder.HasOne<Collection>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.OriginalCollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountableDocument>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.OriginalDocumentId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountableDocument>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.ReplacementDocumentId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Collection>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.ReplacementCollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
