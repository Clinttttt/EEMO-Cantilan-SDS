using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

/// <summary>Append-only administrative review events over posted Collections. Restrict-delete: a Collection with review history is never removed.</summary>
public sealed class DirectCollectionReviewEventConfiguration : IEntityTypeConfiguration<DirectCollectionReviewEvent>
{
    public void Configure(EntityTypeBuilder<DirectCollectionReviewEvent> b)
    {
        b.ToTable("DirectCollectionReviewEvents", t => t.HasCheckConstraint("CK_DirectCollectionReview_Note",
            "(\"Outcome\" <> 2) OR (\"Note\" IS NOT NULL AND length(btrim(\"Note\")) > 0)"));
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        b.HasIndex(x => new { x.MunicipalityId, x.CollectionId, x.OccurredAtUtc });
        b.HasOne<Collection>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.CollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Outcome).HasConversion<int>();
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.ActorName).HasMaxLength(100);
    }
}
