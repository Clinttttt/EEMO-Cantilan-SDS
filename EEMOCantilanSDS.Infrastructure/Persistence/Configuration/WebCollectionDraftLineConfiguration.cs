using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class WebCollectionDraftLineConfiguration : IEntityTypeConfiguration<WebCollectionDraftLine>
{
    public void Configure(EntityTypeBuilder<WebCollectionDraftLine> builder)
    {
        builder.ToTable("WebCollectionDraftLines", table =>
        {
            table.HasCheckConstraint("CK_WebCollectionDraftLines_Amount_Positive", "\"Amount\" > 0");
            table.HasCheckConstraint(
                "CK_WebCollectionDraftLines_SourceShape",
                "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) " +
                "OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) " +
                "OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4, 5)) " +
                "OR (\"SourceKind\" IN (1, 4, 5, 6, 7, 9) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.DraftId).IsRequired();
        builder.Property(x => x.LineOrder).IsRequired();
        builder.Property(x => x.RevenueClassificationId).IsRequired();
        builder.Property(x => x.RevenueClassificationPolicyId).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(300);
        builder.Property(x => x.SourceKind).HasConversion<int?>();
        builder.Property(x => x.SourceId);
        builder.Property(x => x.SourcePart).HasConversion<int?>();
        builder.Property(x => x.CalculationSnapshot).HasColumnType("text");
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy).HasMaxLength(100);
        builder.HasIndex(x => new { x.MunicipalityId, x.DraftId, x.LineOrder }).IsUnique();
        builder.HasOne<WebCollectionDraft>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.DraftId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RevenueClassification>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.RevenueClassificationId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RevenueClassificationPolicy>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.RevenueClassificationId, x.RevenueClassificationPolicyId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.RevenueClassificationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
