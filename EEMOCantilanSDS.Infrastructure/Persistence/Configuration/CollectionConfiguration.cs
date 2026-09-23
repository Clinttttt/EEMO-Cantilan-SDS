using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections", table => table.HasCheckConstraint(
            "CK_Collections_TotalAmount_Positive",
            "\"TotalAmount\" > 0"));

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });

        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.BusinessDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.RecordedAtUtc).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ActorRole).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CollectorId);
        builder.Property(x => x.PayorUserId);
        builder.Property(x => x.PayerName).HasMaxLength(200);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ClientOperationId);

        builder.HasIndex(x => new { x.MunicipalityId, x.BusinessDate });
        builder.HasIndex(x => new { x.MunicipalityId, x.CollectorId, x.BusinessDate });
        builder.HasIndex(x => x.ClientOperationId)
            .IsUnique()
            .HasFilter("\"ClientOperationId\" IS NOT NULL");

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => new { x.MunicipalityId, x.CollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
