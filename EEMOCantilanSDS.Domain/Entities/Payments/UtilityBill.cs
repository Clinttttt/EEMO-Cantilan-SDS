using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
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
        public SettlementAuthority ElectricitySettlementAuthorityState { get; private set; } = SettlementAuthority.Legacy;
        public SettlementAuthority WaterSettlementAuthorityState { get; private set; } = SettlementAuthority.Legacy;
        public Guid? ElectricitySettlementCutoverId { get; private set; }
        public Guid? WaterSettlementCutoverId { get; private set; }

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

        /// <summary>True when this bill belongs to the given billing month.</summary>
        public bool IsForMonth(int year, int month) => BillingYear == year && BillingMonth == month;

        /// <summary>
        /// The bills a collector must still be able to answer for while standing at the stall in the given
        /// month: that month's bills, and every earlier bill that is still owed. An unpaid utility bill does
        /// not stop being collectible because the month turned over — the office's arrears say it is owed, so
        /// the field app must be able to see and settle it. Ordered oldest month first, so the longest-standing
        /// bill is the one settled first.
        ///
        /// Owed is tested per utility, not on the bill's net balance: electricity over-collected by a mistaken
        /// partial would otherwise mask water that is genuinely still owed on the same bill.
        /// </summary>
        public static IReadOnlyList<UtilityBill> MonthAndStillOwed(IEnumerable<UtilityBill> bills, int year, int month) =>
            bills
                .Where(b => b.IsForMonth(year, month)
                         || (IsBefore(b, year, month) && (b.ElecBalanceDue > 0m || b.WaterBalanceDue > 0m)))
                .OrderBy(b => b.BillingYear).ThenBy(b => b.BillingMonth)
                .ToList();

        private static bool IsBefore(UtilityBill bill, int year, int month) =>
            bill.BillingYear < year || (bill.BillingYear == year && bill.BillingMonth < month);

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

        /// <summary>
        /// A utility whose payment is already recorded has its readings/rate LOCKED — they determined the
        /// receipted charge, so changing them would silently alter a paid/receipted amount. Returns true when
        /// the proposed values would change a settled utility's readings (checked per-utility, so the still-
        /// unpaid side stays editable), letting the caller reject the edit instead of corrupting history.
        /// </summary>
        public bool WouldChangeSettledReadings(
            decimal elecPreviousReading, decimal elecCurrentReading, decimal elecRatePerKwh,
            decimal waterPreviousReading, decimal waterCurrentReading, decimal waterRatePerCubicMeter)
        {
            var elecChanged = elecPreviousReading != ElecPreviousReading
                || elecCurrentReading != ElecCurrentReading
                || elecRatePerKwh != ElecRatePerKwh;
            var waterChanged = waterPreviousReading != WaterPreviousReading
                || waterCurrentReading != WaterCurrentReading
                || waterRatePerCubicMeter != WaterRatePerCubicMeter;

            return (elecChanged && ElecStatus != PaymentStatus.Unpaid)
                || (waterChanged && WaterStatus != PaymentStatus.Unpaid);
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
            var nextElecStatus = Normalize(elecStatus, elecPartialAmount ?? 0m, ElecCharge, out var elecPartial);
            var nextWaterStatus = Normalize(waterStatus, waterPartialAmount ?? 0m, WaterCharge, out var waterPartial);
            var nextElecOr = nextElecStatus == PaymentStatus.Unpaid ? null
                : string.IsNullOrWhiteSpace(elecOrNumber) ? ElecORNumber : elecOrNumber;
            var nextWaterOr = nextWaterStatus == PaymentStatus.Unpaid ? null
                : string.IsNullOrWhiteSpace(waterOrNumber) ? WaterORNumber : waterOrNumber;

            EnsureLegacyUtilityWriter(ElectricitySettlementAuthorityState,
                nextElecStatus == ElecStatus && elecPartial == ElecPartialAmount && nextElecOr == ElecORNumber,
                "electricity");
            EnsureLegacyUtilityWriter(WaterSettlementAuthorityState,
                nextWaterStatus == WaterStatus && waterPartial == WaterPartialAmount && nextWaterOr == WaterORNumber,
                "water");

            ElecStatus = nextElecStatus;
            ElecPartialAmount = elecPartial;
            WaterStatus = nextWaterStatus;
            WaterPartialAmount = waterPartial;

            // Per-utility OR: keep on payment, clear when reset to Unpaid.
            ElecORNumber = nextElecOr;

            WaterORNumber = nextWaterOr;

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
        public void SetClientOperationId(Guid clientOperationId)
        {
            if (ElectricitySettlementAuthorityState != SettlementAuthority.Legacy
                || WaterSettlementAuthorityState != SettlementAuthority.Legacy)
                throw new InvalidOperationException("Legacy idempotency writers are disabled once cutover begins.");
            if (clientOperationId == Guid.Empty)
                throw new ArgumentException("Client operation id must be valid.", nameof(clientOperationId));
            ClientOperationId = clientOperationId;
        }

        private static void EnsureLegacyUtilityWriter(
            SettlementAuthority authority, bool unchanged, string utility)
        {
            if (authority != SettlementAuthority.Legacy && !unchanged)
                throw new InvalidOperationException($"Legacy {utility} settlement writers are disabled once cutover begins.");
        }

        public void MarkElectricityPendingCutover()
        {
            if (ElectricitySettlementAuthorityState != SettlementAuthority.Legacy)
                throw new InvalidOperationException("Only Legacy electricity settlement can enter Pending Cutover.");
            ElectricitySettlementAuthorityState = SettlementAuthority.PendingCutover;
        }

        public void ActivateCanonicalElectricitySettlement(CollectionSettlementCutover cutover)
        {
            ArgumentNullException.ThrowIfNull(cutover);
            if (ElectricitySettlementAuthorityState != SettlementAuthority.PendingCutover)
                throw new InvalidOperationException("Electricity reconciliation must complete before Canonical activation.");
            if (cutover.SourceKind != CollectionSourceKind.UtilityBill || cutover.SourceId != Id
                || cutover.SourcePart != CollectionSourcePart.Electricity
                || (MunicipalityId != Guid.Empty && cutover.MunicipalityId != MunicipalityId))
                throw new InvalidOperationException("The frozen cutover does not identify this tenant's electricity source.");
            ElectricitySettlementCutoverId = cutover.Id;
            ElectricitySettlementAuthorityState = SettlementAuthority.Canonical;
        }

        public void MarkWaterPendingCutover()
        {
            if (WaterSettlementAuthorityState != SettlementAuthority.Legacy)
                throw new InvalidOperationException("Only Legacy water settlement can enter Pending Cutover.");
            WaterSettlementAuthorityState = SettlementAuthority.PendingCutover;
        }

        public void ActivateCanonicalWaterSettlement(CollectionSettlementCutover cutover)
        {
            ArgumentNullException.ThrowIfNull(cutover);
            if (WaterSettlementAuthorityState != SettlementAuthority.PendingCutover)
                throw new InvalidOperationException("Water reconciliation must complete before Canonical activation.");
            if (cutover.SourceKind != CollectionSourceKind.UtilityBill || cutover.SourceId != Id
                || cutover.SourcePart != CollectionSourcePart.Water
                || (MunicipalityId != Guid.Empty && cutover.MunicipalityId != MunicipalityId))
                throw new InvalidOperationException("The frozen cutover does not identify this tenant's water source.");
            WaterSettlementCutoverId = cutover.Id;
            WaterSettlementAuthorityState = SettlementAuthority.Canonical;
        }
    }
}
