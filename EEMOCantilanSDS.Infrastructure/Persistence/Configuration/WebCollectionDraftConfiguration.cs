using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class WebCollectionDraftConfiguration : IEntityTypeConfiguration<WebCollectionDraft>
{
    public void Configure(EntityTypeBuilder<WebCollectionDraft> builder)
    {
        builder.ToTable("WebCollectionDrafts", table => table.HasCheckConstraint(
            "CK_WebCollectionDrafts_Revision_Positive", "\"Revision\" > 0"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.OwnerUserId).IsRequired();
        builder.Property(x => x.PayorId);
        builder.Property(x => x.PayerNameSnapshot).HasMaxLength(200);
        builder.Property(x => x.BusinessDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.InstrumentFamily).HasConversion<int?>();
        builder.Property(x => x.AccountableDocumentId);
        builder.Property(x => x.Revision).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.ReviewedRevision);
        builder.Property(x => x.ReviewedFingerprint).HasMaxLength(64);
        builder.Property(x => x.ReviewedAtUtc);
        builder.Property(x => x.ReviewedByUserId);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.CollectionId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy).HasMaxLength(100);
        builder.HasIndex(x => new { x.MunicipalityId, x.OwnerUserId, x.Status });
        builder.HasIndex(x => new { x.MunicipalityId, x.CollectionId }).IsUnique()
            .HasFilter("\"CollectionId\" IS NOT NULL");
        builder.HasOne<Payor>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.PayorId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountableDocument>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.AccountableDocumentId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Collection>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.CollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
