using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class UtilityBillConfiguration : IEntityTypeConfiguration<UtilityBill>
    {
        public void Configure(EntityTypeBuilder<UtilityBill> builder)
        {
            builder.ToTable("UtilityBills");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.StallId).IsRequired();
            builder.Property(x => x.CollectorId).IsRequired(false);
            builder.Property(x => x.BillingYear).IsRequired();
            builder.Property(x => x.BillingMonth).IsRequired();

            builder.Property(x => x.ElecStatus).IsRequired().HasConversion<int>();
            builder.Property(x => x.WaterStatus).IsRequired().HasConversion<int>();
            builder.Property(x => x.ElecORNumber).HasMaxLength(50);
            builder.Property(x => x.WaterORNumber).HasMaxLength(50);
            builder.Property(x => x.ElecPaidAt);
            builder.Property(x => x.WaterPaidAt);
            builder.Property(x => x.PaidAt);

            // Readings (kWh / cu.m) and per-bill rates.
            builder.Property(x => x.ElecPreviousReading).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.ElecCurrentReading).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.ElecRatePerKwh).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.ElecPartialAmount).IsRequired().HasPrecision(18, 2).HasDefaultValue(0);
            builder.Property(x => x.WaterPreviousReading).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.WaterCurrentReading).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.WaterRatePerCubicMeter).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.WaterPartialAmount).IsRequired().HasPrecision(18, 2).HasDefaultValue(0);

            builder.Property(x => x.Remarks).HasColumnType("text");

            // Computed properties are not persisted.
            builder.Ignore(x => x.ElecConsumption);
            builder.Ignore(x => x.WaterConsumption);
            builder.Ignore(x => x.ElecCharge);
            builder.Ignore(x => x.WaterCharge);
            builder.Ignore(x => x.TotalCharge);
            builder.Ignore(x => x.ElecAmountPaid);
            builder.Ignore(x => x.WaterAmountPaid);
            builder.Ignore(x => x.AmountPaid);
            builder.Ignore(x => x.BalanceDue);
            builder.Ignore(x => x.ElecBalanceDue);
            builder.Ignore(x => x.WaterBalanceDue);
            builder.Ignore(x => x.Status);
            builder.Ignore(x => x.PeriodKey);

            // One bill per stall per billing month.
            builder.HasIndex(x => new { x.StallId, x.BillingYear, x.BillingMonth }).IsUnique();

            // Offline-sync idempotency: a client operation id maps to at most one bill (DB backstop).
            builder.HasIndex(x => x.ClientOperationId)
                .IsUnique()
                .HasFilter("\"ClientOperationId\" IS NOT NULL");

            // Same-table OR-uniqueness backstops per utility (global cross-column/cross-table uniqueness
            // is enforced in the app layer, which also allows one receipt to cover both utilities on a bill).
            builder.HasIndex(x => x.ElecORNumber)
                .IsUnique()
                .HasFilter("\"ElecORNumber\" IS NOT NULL AND \"ElecORNumber\" <> ''");

            builder.HasIndex(x => x.WaterORNumber)
                .IsUnique()
                .HasFilter("\"WaterORNumber\" IS NOT NULL AND \"WaterORNumber\" <> ''");

            builder.HasOne(x => x.Stall)
                .WithMany()
                .HasForeignKey(x => x.StallId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
