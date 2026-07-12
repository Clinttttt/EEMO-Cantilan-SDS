using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class DailyCollectionConfiguration : IEntityTypeConfiguration<DailyCollection>
    {
        public void Configure(EntityTypeBuilder<DailyCollection> builder)
        {
            builder.ToTable("DailyCollections");

            builder.HasKey(s => s.Id);

            builder.Property(s => s.StallId)
                .IsRequired();

            builder.Property(s => s.CollectorId);

            builder.Property(s => s.CollectionDate)
                .IsRequired();

            builder.Property(x => x.DailyFee)
                   .HasPrecision(18, 2)
                   .HasDefaultValue(30.00m);

            builder.Property(s=> s.IsPaid)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(s => s.IsAbsent)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(s => s.ORNumber)
                .HasMaxLength(50);

            builder.Property(s=> s.FishKilos)
                .HasPrecision(18, 2);

            builder.Ignore(x => x.FishFeeAmount);
            builder.Ignore(x => x.TotalCollected);

            builder.HasIndex(x => new { x.StallId, x.CollectionDate }).IsUnique();

            // OR uniqueness is enforced in the application layer (OrNumberRegistry), which — like the
            // slaughterhouse — allows one receipt (OR) to cover multiple daily collections of the SAME
            // stall while rejecting reuse across different stalls/modules. A plain unique index cannot
            // express "same OR ⇒ same stall", so this is a NON-unique lookup index only.
            builder.HasIndex(x => new { x.MunicipalityId, x.ORNumber })
                .HasFilter("\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");

            // Offline-sync idempotency: a client operation id maps to at most one record (DB backstop
            // so a replayed/duplicated offline collection is never created twice).
            builder.HasIndex(x => x.ClientOperationId)
                .IsUnique()
                .HasFilter("\"ClientOperationId\" IS NOT NULL");

            builder.HasOne(s => s.Stall)
                .WithMany()
                .HasForeignKey(s => s.StallId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.CreatedBy).HasMaxLength(100);
            builder.Property(x => x.UpdatedAt);
            builder.Property(x => x.UpdatedBy).HasMaxLength(100);
            builder.Property(x => x.IsDeleted).HasDefaultValue(false);
            builder.Property(x => x.DeletedAt);
            builder.Property(x => x.DeletedBy).HasMaxLength(100);
        }
    }
}
