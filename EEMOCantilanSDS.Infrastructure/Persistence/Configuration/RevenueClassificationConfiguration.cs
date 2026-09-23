using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class RevenueClassificationConfiguration : IEntityTypeConfiguration<RevenueClassification>
{
    public void Configure(EntityTypeBuilder<RevenueClassification> builder)
    {
        builder.ToTable("RevenueClassifications");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });

        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.SemanticCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.SemanticCode }).IsUnique();

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy).HasMaxLength(100);
    }
}
