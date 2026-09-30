using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class CollectionAllocationConfiguration : IEntityTypeConfiguration<CollectionAllocation>
{
    public void Configure(EntityTypeBuilder<CollectionAllocation> builder)
    {
        builder.ToTable("CollectionAllocations", table =>
        {
            table.HasCheckConstraint("CK_CollectionAllocations_Amount_Positive", "\"Amount\" > 0");
            table.HasCheckConstraint(
                "CK_CollectionAllocations_SourceShape",
                "((\"SourceKind\" = 3 AND \"SourcePart\" IN (1, 2)) OR " +
                "(\"SourceKind\" = 2 AND \"SourcePart\" IN (3, 4, 5)) OR " +
                "(\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourcePart\" IS NULL))");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.CollectionLineId).IsRequired();
        builder.Property(x => x.SourceKind).HasConversion<int>().IsRequired();
        builder.Property(x => x.SourceId).IsRequired();
        builder.Property(x => x.SourcePart).HasConversion<int?>();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.SourceSnapshot).HasColumnType("text");
        builder.HasIndex(x => new { x.MunicipalityId, x.CollectionLineId });
        builder.HasIndex(x => new { x.MunicipalityId, x.SourceKind, x.SourceId, x.SourcePart });
        builder.HasOne<CollectionLine>().WithMany(x => x.Allocations)
            .HasForeignKey(x => new { x.MunicipalityId, x.CollectionLineId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
