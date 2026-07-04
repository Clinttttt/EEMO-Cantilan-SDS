using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
    {
        public void Configure(EntityTypeBuilder<PaymentRecord> builder)
        {
            builder.ToTable("PaymentRecords");

            // Offline-sync idempotency: a client operation id maps to at most one record (DB backstop).
            builder.HasIndex(x => x.ClientOperationId)
                .IsUnique()
                .HasFilter("\"ClientOperationId\" IS NOT NULL");

            builder.HasKey(s => s.Id);

            builder.Property(s => s.StallId)
                .IsRequired();

            builder.Property(s => s.CollectorId)
                .IsRequired(false);

            builder.Property(s => s.BillingYear)
                .IsRequired();

            builder.Property(s => s.BillingMonth)
                .IsRequired();

            builder.Property(s => s.Status)
                .IsRequired()
                .HasConversion<int>();
             

            builder.Property(s => s.ORNumber)
                .HasMaxLength(50);

            builder.Property(s => s.PaidAt);

            builder.Property(s=> s.BaseRentalAmount)
                .IsRequired()
                .HasPrecision(18, 2);

            builder.Property(s => s.PartialAmount)
                .IsRequired()
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            builder.Property(x => x.ElecReading)
                .HasPrecision(18, 2);
            builder.Property(x => x.ElecAmount)
                .HasPrecision(18, 2);
            builder.Property(x => x.WaterReading)
                .HasPrecision(18, 2);
            builder.Property(x => x.WaterAmount)
                .HasPrecision(18, 2);

            builder.Property(x => x.FishKilos)
                .HasPrecision(18, 3);

            builder.Property(x => x.Remarks)
                .HasColumnType("text");

            builder.Ignore(x => x.PeriodKey);
            builder.Ignore(x => x.TotalBill);
            builder.Ignore(x => x.AmountPaid);
            builder.Ignore(x => x.BalanceDue);
            builder.Ignore(x => x.FishFeeAmount);

            builder.HasIndex(x => new { x.StallId, x.BillingYear, x.BillingMonth })
                .IsUnique();

            // Same-table backstop for OR uniqueness (concurrency safety net).
            // Global cross-table uniqueness is enforced in the application layer.
            builder.HasIndex(x => new { x.MunicipalityId, x.ORNumber })
                .IsUnique()
                .HasFilter("\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");

            builder.HasOne(s => s.Stall)
                .WithMany()
                .HasForeignKey(s => s.StallId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
