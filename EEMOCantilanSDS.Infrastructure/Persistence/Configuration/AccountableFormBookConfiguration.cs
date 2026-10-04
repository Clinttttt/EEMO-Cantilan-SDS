using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class AccountableFormBookConfiguration : IEntityTypeConfiguration<AccountableFormBook>
{
    public void Configure(EntityTypeBuilder<AccountableFormBook> builder)
    {
        builder.ToTable("AccountableFormBooks", table => table.HasCheckConstraint(
            "CK_AccountableFormBooks_Range", "\"FirstSerialNumber\" >= 0 AND \"LastSerialNumber\" >= \"FirstSerialNumber\" AND \"SerialWidth\" BETWEEN 1 AND 30"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.InstrumentType).HasConversion<int>().IsRequired();
        builder.Property(x => x.SeriesName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NumberPrefix).HasMaxLength(30).IsRequired();
        builder.Property(x => x.FirstSerialNumber).IsRequired();
        builder.Property(x => x.LastSerialNumber).IsRequired();
        builder.Property(x => x.SerialWidth).IsRequired();
        builder.Property(x => x.NumberSuffix).HasMaxLength(30).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(x => x.FormVariant).HasMaxLength(20).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(x => x.ReceivedOn);
        builder.Property(x => x.SourceAuthority).HasMaxLength(100);
        builder.Property(x => x.SourceReference).HasMaxLength(100);
        builder.Ignore(x => x.Quantity);
        builder.Property(x => x.ReceivedAtUtc).IsRequired();
        builder.Property(x => x.ReceivedByActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy).HasMaxLength(100);
    }
}
