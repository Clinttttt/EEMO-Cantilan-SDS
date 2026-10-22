using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class TerminalSectionSettingConfiguration : IEntityTypeConfiguration<TerminalSectionSetting>
{
    public void Configure(EntityTypeBuilder<TerminalSectionSetting> b)
    {
        b.ToTable("TerminalSectionSettings");
        b.HasKey(x => x.Id);
        b.Property(x => x.MunicipalityId).IsRequired();
        b.Property(x => x.Section).IsRequired();
        b.Property(x => x.IsEnabled).IsRequired();
        b.Property(x => x.LocalDisplayName).HasMaxLength(80);
        b.Property(x => x.Version).IsRequired().HasDefaultValue(1L).IsConcurrencyToken();
        b.HasIndex(x => new { x.MunicipalityId, x.Section }).IsUnique();
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedAt);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.Property(x => x.IsDeleted).HasDefaultValue(false);
        b.Property(x => x.DeletedAt);
        b.Property(x => x.DeletedBy).HasMaxLength(100);
    }
}
