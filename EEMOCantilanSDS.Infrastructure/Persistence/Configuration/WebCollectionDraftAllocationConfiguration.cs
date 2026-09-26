using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class WebCollectionDraftAllocationConfiguration : IEntityTypeConfiguration<WebCollectionDraftAllocation>
{
    public void Configure(EntityTypeBuilder<WebCollectionDraftAllocation> builder)
    {
        builder.ToTable("WebCollectionDraftAllocations", table =>
        {
            table.HasCheckConstraint("CK_WebCollectionDraftAllocations_Amount_Positive", "\"Amount\" > 0");
            table.HasCheckConstraint(
                "CK_WebCollectionDraftAllocations_SourceShape",
                "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR " +
                "(\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4)) OR " +
                "(\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.DraftLineId).IsRequired();
        builder.Property(x => x.SourceKind).HasConversion<int>().IsRequired();
        builder.Property(x => x.SourceId).IsRequired();
        builder.Property(x => x.SourcePart).HasConversion<int?>();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.SourceSnapshot).HasColumnType("text");
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy).HasMaxLength(100);
        builder.HasIndex(x => new { x.MunicipalityId, x.DraftLineId });
        builder.HasOne<WebCollectionDraftLine>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.DraftLineId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
