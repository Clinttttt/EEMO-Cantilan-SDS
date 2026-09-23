using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class CollectionLineConfiguration : IEntityTypeConfiguration<CollectionLine>
{
    public void Configure(EntityTypeBuilder<CollectionLine> builder)
    {
        builder.ToTable("CollectionLines", table =>
        {
            table.HasCheckConstraint("CK_CollectionLines_Amount_Positive", "\"Amount\" > 0");
            table.HasCheckConstraint(
                "CK_CollectionLines_SourceShape",
                "((\"SourceKind\" IS NULL AND \"SourceId\" IS NULL AND \"SourcePart\" IS NULL) " +
                "OR (\"SourceKind\" = 3 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (1, 2)) " +
                "OR (\"SourceKind\" = 2 AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IN (3, 4)) " +
                "OR (\"SourceKind\" IN (1, 4, 5, 6, 7) AND \"SourceId\" IS NOT NULL AND \"SourcePart\" IS NULL))");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.CollectionId).IsRequired();
        builder.Property(x => x.RevenueClassificationId).IsRequired();
        builder.Property(x => x.RevenueClassificationPolicyId).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.SourceKind).HasConversion<int?>();
        builder.Property(x => x.SourceId);
        builder.Property(x => x.SourcePart).HasConversion<int?>();

        builder.HasIndex(x => new { x.MunicipalityId, x.CollectionId });
        builder.HasIndex(x => new
        {
            x.MunicipalityId,
            x.RevenueClassificationId,
            x.RevenueClassificationPolicyId
        });
        builder.HasIndex(x => new { x.MunicipalityId, x.SourceKind, x.SourceId, x.SourcePart });

        builder.HasOne<RevenueClassification>()
            .WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.RevenueClassificationId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RevenueClassificationPolicy>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.MunicipalityId,
                x.RevenueClassificationId,
                x.RevenueClassificationPolicyId
            })
            .HasPrincipalKey(x => new
            {
                x.MunicipalityId,
                x.RevenueClassificationId,
                x.Id
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
