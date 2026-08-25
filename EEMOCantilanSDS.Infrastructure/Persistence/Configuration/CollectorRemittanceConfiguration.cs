using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class CollectorRemittanceConfiguration : IEntityTypeConfiguration<CollectorRemittance>
    {
        public void Configure(EntityTypeBuilder<CollectorRemittance> builder)
        {
            builder.ToTable("CollectorRemittances");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.MunicipalityId).IsRequired();
            builder.Property(x => x.CollectorId).IsRequired();
            builder.Property(x => x.Amount).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.ReceivedAt).IsRequired();
            builder.Property(x => x.CoversFrom).IsRequired();
            builder.Property(x => x.CoversTo).IsRequired();
            builder.Property(x => x.ReceivedById).IsRequired();
            builder.Property(x => x.ReceivedByName).IsRequired().HasMaxLength(120);
            builder.Property(x => x.ReferenceNo).HasMaxLength(60);
            builder.Property(x => x.Notes).HasMaxLength(400);
            builder.Property(x => x.VoidReason).HasMaxLength(400);

            // Read one collector's remittances for a period, which is every query this record serves.
            builder.HasIndex(x => new { x.MunicipalityId, x.CollectorId, x.CoversFrom });

            builder.HasOne(x => x.Collector)
                .WithMany()
                .HasForeignKey(x => x.CollectorId)
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
