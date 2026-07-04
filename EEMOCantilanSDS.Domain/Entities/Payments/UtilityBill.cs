using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    /// <summary>
    /// End-of-month, meter-based electricity &amp; water bill for an NPM stall. Consumption is the
    /// difference between the previous and current readings; the per-unit rate is entered per bill,
    /// and the charge is consumption × rate. Electricity and water are settled <b>independently</b>
    /// (a payor may pay one before the other), so each carries its own payment status and partial
    /// amount; the overall <see cref="Status"/> is derived from the two. Kept separate from the daily
    /// stall fee and fish-kilo fee. Readings/rates are entered by an admin; payment can be collected by
    /// an admin (web) or a collector (mobile, incl. offline replay via <see cref="ClientOperationId"/>).
    /// </summary>
    public class UtilityBill : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public Guid StallId { get; private set; }
        // Who collected the (latest) payment (a collector). Null when an admin collected.
        public Guid? CollectorId { get; private set; }
        public int BillingYear { get; private set; }
        public int BillingMonth { get; private set; }

        // Electricity (kWh). Rate entered per bill.
        public decimal ElecPreviousReading { get; private set; }
        public decimal ElecCurrentReading { get; private set; }
        public decimal ElecRatePerKwh { get; private set; }
        public PaymentStatus ElecStatus { get; private set; } = PaymentStatus.Unpaid;
        public decimal ElecPartialAmount { get; private set; }

        // Water (cubic metres). Rate entered per bill.
        public decimal WaterPreviousReading { get; private set; }
        public decimal WaterCurrentReading { get; private set; }
        public decimal WaterRatePerCubicMeter { get; private set; }
        public PaymentStatus WaterStatus { get; private set; } = PaymentStatus.Unpaid;
        public decimal WaterPartialAmount { get; private set; }

        // Each utility carries its own OR number: a payor may settle electricity and water with
        // separate receipts (or the same one when paid together). Cleared when that utility is Unpaid.
        public string? ElecORNumber { get; private set; }
        public string? WaterORNumber { get; private set; }
        // Independent settlement times: each utility stamps its own paid-at on first settlement and keeps
        // it on re-marks; cleared when that utility is reset to Unpaid. PaidAt is the overall latest.
        public DateTime? ElecPaidAt { get; private set; }
        public DateTime? WaterPaidAt { get; private set; }
        public DateTime? PaidAt { get; private set; }
        public string? Remarks { get; private set; }
        public Guid? ClientOperationId { get; private set; }

        // ── Computed (never negative; a lower current reading yields zero, not a credit) ──
        public decimal ElecConsumption => Math.Max(0m, ElecCurrentReading - ElecPreviousReading);
        public decimal WaterConsumption => Math.Max(0m, WaterCurrentReading - WaterPreviousReading);
        public decimal ElecCharge => ElecConsumption * ElecRatePerKwh;
        public decimal WaterCharge => WaterConsumption * WaterRatePerCubicMeter;
        public decimal TotalCharge => ElecCharge + WaterCharge;

        public decimal ElecAmountPaid => ElecStatus == PaymentStatus.Paid ? ElecCharge
                                       : ElecStatus == PaymentStatus.Partial ? ElecPartialAmount : 0m;
        public decimal WaterAmountPaid => WaterStatus == PaymentStatus.Paid ? WaterCharge
                                        : WaterStatus == PaymentStatus.Partial ? WaterPartialAmount : 0m;
        public decimal AmountPaid => ElecAmountPaid + WaterAmountPaid;
        public decimal BalanceDue => TotalCharge - AmountPaid;
        public decimal ElecBalanceDue => ElecCharge - ElecAmountPaid;
        public decimal WaterBalanceDue => WaterCharge - WaterAmountPaid;

        /// <summary>Overall bill status derived from the two utilities: Paid only when both are paid.</summary>
        public PaymentStatus Status =>
            ElecStatus == PaymentStatus.Paid && WaterStatus == PaymentStatus.Paid ? PaymentStatus.Paid
            : AmountPaid <= 0m ? PaymentStatus.Unpaid
            : PaymentStatus.Partial;

        public string PeriodKey => $"{BillingYear:0000}-{BillingMonth:00}";

        public Stall? Stall { get; private set; }

        private UtilityBill() { }

        public static UtilityBill Create(
            Guid stallId,
            int billingYear,
            int billingMonth,
            decimal elecPreviousReading,
            decimal elecCurrentReading,
            decimal elecRatePerKwh,
            decimal waterPreviousReading,
            decimal waterCurrentReading,
            decimal waterRatePerCubicMeter,
            string createdBy = "System")
        {
            return new UtilityBill
            {
                Id = Guid.NewGuid(),
                StallId = stallId,
                BillingYear = billingYear,
                BillingMonth = billingMonth,
                ElecPreviousReading = elecPreviousReading,
                ElecCurrentReading = elecCurrentReading,
                ElecRatePerKwh = elecRatePerKwh,
                WaterPreviousReading = waterPreviousReading,
                WaterCurrentReading = waterCurrentReading,
                WaterRatePerCubicMeter = waterRatePerCubicMeter,
                ElecStatus = PaymentStatus.Unpaid,
                WaterStatus = PaymentStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        /// <summary>Admin edits the readings/rates (charges recompute automatically; payment untouched).</summary>
        public void UpdateReadings(
            decimal elecPreviousReading,
            decimal elecCurrentReading,
            decimal elecRatePerKwh,
            decimal waterPreviousReading,
            decimal waterCurrentReading,
            decimal waterRatePerCubicMeter,
            string? remarks,
            string updatedBy = "System")
        {
            ElecPreviousReading = elecPreviousReading;
            ElecCurrentReading = elecCurrentReading;
            ElecRatePerKwh = elecRatePerKwh;
            WaterPreviousReading = waterPreviousReading;
            WaterCurrentReading = waterCurrentReading;
            WaterRatePerCubicMeter = waterRatePerCubicMeter;
            Remarks = remarks;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>
        /// Records the electricity and water collection independently. A partial amount that meets or
        /// exceeds that utility's charge auto-upgrades to Paid. <paramref name="collectorId"/> is null for
        /// an admin-recorded payment. Each utility keeps its own OR number; a utility reset to Unpaid clears
        /// its OR. Clearing both to Unpaid also resets collector/paid-at.
        /// </summary>
        public void RecordPayment(
            string? elecOrNumber,
            string? waterOrNumber,
            Guid? collectorId,
            PaymentStatus elecStatus,
            decimal? elecPartialAmount,
            PaymentStatus waterStatus,
            decimal? waterPartialAmount,
            string? remarks = null,
            string updatedBy = "System")
        {
            ElecStatus = Normalize(elecStatus, elecPartialAmount ?? 0m, ElecCharge, out var elecPartial);
            ElecPartialAmount = elecPartial;
            WaterStatus = Normalize(waterStatus, waterPartialAmount ?? 0m, WaterCharge, out var waterPartial);
            WaterPartialAmount = waterPartial;

            // Per-utility OR: keep on payment, clear when reset to Unpaid.
            if (ElecStatus == PaymentStatus.Unpaid) ElecORNumber = null;
            else if (!string.IsNullOrWhiteSpace(elecOrNumber)) ElecORNumber = elecOrNumber;

            if (WaterStatus == PaymentStatus.Unpaid) WaterORNumber = null;
            else if (!string.IsNullOrWhiteSpace(waterOrNumber)) WaterORNumber = waterOrNumber;

            // Per-utility paid-at: stamp on first settlement, preserve on re-marks, clear when reset to Unpaid.
            if (ElecStatus == PaymentStatus.Unpaid) ElecPaidAt = null;
            else if (ElecPaidAt is null) ElecPaidAt = DateTime.UtcNow;

            if (WaterStatus == PaymentStatus.Unpaid) WaterPaidAt = null;
            else if (WaterPaidAt is null) WaterPaidAt = DateTime.UtcNow;

            var anyPayment = ElecStatus != PaymentStatus.Unpaid || WaterStatus != PaymentStatus.Unpaid;
            if (!anyPayment)
            {
                CollectorId = null;
                PaidAt = null;
            }
            else
            {
                if (collectorId.HasValue) CollectorId = collectorId;
                PaidAt = DateTime.UtcNow;
            }

            if (remarks is not null) Remarks = remarks;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        private static PaymentStatus Normalize(PaymentStatus status, decimal partial, decimal charge, out decimal partialOut)
        {
            if (status == PaymentStatus.Partial && partial >= charge && charge > 0m)
            {
                partialOut = 0m;
                return PaymentStatus.Paid;
            }
            partialOut = status == PaymentStatus.Partial ? partial : 0m;
            return status;
        }

        /// <summary>Stamps the offline-sync idempotency key (set once when replaying a queued offline payment).</summary>
        public void SetClientOperationId(Guid clientOperationId) => ClientOperationId = clientOperationId;
    }
}
