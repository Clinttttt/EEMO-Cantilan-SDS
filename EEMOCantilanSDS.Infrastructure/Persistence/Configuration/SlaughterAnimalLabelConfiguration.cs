using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class SlaughterAnimalLabelConfiguration : IEntityTypeConfiguration<SlaughterAnimalLabel>
{
    public void Configure(EntityTypeBuilder<SlaughterAnimalLabel> builder)
    {
        builder.ToTable("SlaughterAnimalLabels");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.AnimalType).IsRequired();
        builder.Property(x => x.DisplayLabel).IsRequired().HasMaxLength(100);
        builder.HasIndex(x => new { x.MunicipalityId, x.AnimalType }).IsUnique();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
    }
}
