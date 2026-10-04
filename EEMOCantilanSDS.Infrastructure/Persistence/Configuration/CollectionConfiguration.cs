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
        builder.Property(x => x.PayorId);
        builder.Property(x => x.PayorUserId);
        builder.Property(x => x.PayerName).HasMaxLength(200);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ClientOperationId);

        // The StallTrack Reference Code (IA-062). The number comes from ONE database sequence (never MAX+1), so concurrent postings
        // always receive distinct numbers; the code is computed from year and number, so it cannot drift from them. The unique index
        // is deliberately global (not per tenant): two collections anywhere in StallTrack can never show the same SRC.
        builder.Property(x => x.ReferenceYear).IsRequired();
        builder.Property(x => x.ReferenceNumber).UseSequence("CollectionReferenceNumberSeq").IsRequired();
        builder.Property(x => x.ReferenceCode).HasMaxLength(40)
            .HasComputedColumnSql("'SRC-' || \"ReferenceYear\"::text || '-' || repeat('0', greatest(6 - length(\"ReferenceNumber\"::text), 0)) || \"ReferenceNumber\"::text", stored: true);
        builder.HasIndex(x => x.ReferenceNumber).IsUnique();
        builder.HasIndex(x => x.ReferenceCode).IsUnique();

        builder.HasIndex(x => new { x.MunicipalityId, x.BusinessDate });
        builder.HasIndex(x => new { x.MunicipalityId, x.CollectorId, x.BusinessDate });
        builder.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId })
            .IsUnique()
            .HasFilter("\"ClientOperationId\" IS NOT NULL");

        builder.HasOne<Payor>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.PayorId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => new { x.MunicipalityId, x.CollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
