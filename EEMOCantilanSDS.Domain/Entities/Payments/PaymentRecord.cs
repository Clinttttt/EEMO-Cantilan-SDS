using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    public class PaymentRecord : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public Guid StallId { get; private set; }
        public Guid? CollectorId { get; private set; }
        public int BillingYear { get; private set; }
        public int BillingMonth { get; private set; }
        public PaymentStatus Status { get; private set; } = PaymentStatus.Unpaid;
        public string? ORNumber { get; private set; }
        public DateTime? PaidAt { get; private set; }

        // Offline-sync idempotency key from the mobile client (null for online records).
        public Guid? ClientOperationId { get; private set; }
        public SettlementAuthority SettlementAuthorityState { get; private set; } = SettlementAuthority.Legacy;
        public Guid? SettlementCutoverId { get; private set; }
        public long SettlementVersion { get; private set; } = 1;

        // Fee breakdown
        public decimal BaseRentalAmount { get; private set; }
        public decimal PartialAmount { get; private set; }

        // Utilities
        public decimal? ElecReading { get; private set; }
        public decimal? ElecAmount { get; private set; }
        public decimal? WaterReading { get; private set; }
        public decimal? WaterAmount { get; private set; }

        // Fish fee — NPM Fish Area only (₱1/kg)
        public decimal? FishKilos { get; private set; }
        public decimal? FishFeeAmount => FishKilos.HasValue ? FishKilos.Value * 1.00m : null;

        // Remarks
        public string? Remarks { get; private set; }

        // Computed
        public string PeriodKey => $"{BillingYear:0000}-{BillingMonth:00}";
        public decimal TotalBill => BaseRentalAmount
                                               + (ElecAmount ?? 0)
                                               + (WaterAmount ?? 0)
                                               + (FishFeeAmount ?? 0);
        public decimal AmountPaid => Status == PaymentStatus.Paid ? TotalBill
                                               : Status == PaymentStatus.Partial ? PartialAmount
                                               : 0;
        public decimal BalanceDue => TotalBill - AmountPaid;

        public Stall? Stall { get; private set; }
        private PaymentRecord() { }
        public static PaymentRecord Create(
            Guid stallId,
            int billingYear,
            int billingMonth,
            decimal baseRental,
            string createdBy = "System")
        {
            return new PaymentRecord
            {
                Id = Guid.NewGuid(),
                StallId = stallId,
                BillingYear = billingYear,
                BillingMonth = billingMonth,
                BaseRentalAmount = baseRental,
                Status = PaymentStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
        public void RecordPayment(
        string orNumber,
        Guid collectorId,
        PaymentStatus status,
        decimal? partialAmount = null,
        decimal? elecReading = null,
        decimal? elecAmount = null,
        decimal? waterReading = null,
        decimal? waterAmount = null,
        decimal? fishKilos = null,
        string? remarks = null,
        string updatedBy = "System")
        {
            EnsureLegacySettlementAuthority();
            ORNumber = orNumber;
            CollectorId = collectorId;
            Status = status;
            PartialAmount = partialAmount ?? 0;
            ElecReading = elecReading;
            ElecAmount = elecAmount;
            WaterReading = waterReading;
            WaterAmount = waterAmount;
            FishKilos = fishKilos;
            Remarks = remarks;
            PaidAt = status != PaymentStatus.Unpaid ? DateTime.UtcNow : null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            BumpSettlementVersion();
        }
        /// <summary>
        /// Clears the collector on a payment brought in from the office's own books. Nobody in the system collected
        /// it, and the column is nullable precisely so that can be said - a zero GUID is not "nobody", and the
        /// transaction feed renders one as though a collector were named.
        /// </summary>
        public void ClearCollectorForImportedHistory() => CollectorId = null;

        /// <summary>
        /// Records the date the office's own books state a historical payment was received, instead of now.
        ///
        /// <para>An imported history is a record of money taken months or years ago. Left at the moment of import,
        /// every row appears in the transaction feed and the dashboard's recent collections as though it had been
        /// collected today - the period-keyed reports stay correct, but any view ordered by date states something
        /// untrue. Only moves the date backwards, and only on a payment that has one.</para>
        /// </summary>
        public void BackdateReceipt(DateTime receivedAt, string updatedBy = "System")
        {
            EnsureLegacySettlementAuthority();
            if (PaidAt is null) return;
            if (receivedAt > PaidAt) return;

            PaidAt = receivedAt;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            BumpSettlementVersion();
        }

        /// <summary>
        /// Attaches a manually-entered OR number to an already-recorded payment without
        /// altering the fee breakdown, status, or original collector attribution.
        /// </summary>
        public void SetOrNumber(string orNumber, string updatedBy = "System")
        {
            EnsureLegacySettlementAuthority();
            ORNumber = orNumber;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>
        /// Marks this record fully Paid from an online (GCash/PayMongo) payment. Per the attribution
        /// rule, online payments carry NO collector (CollectorId stays null — captured in audit instead),
        /// and the OR number is left null until staff encode it. Clearing the balance here is what makes
        /// delinquency recompute as cleared for this period.
        /// </summary>
        public void MarkPaidOnline(string remarks, string updatedBy = "Online")
        {
            EnsureLegacySettlementAuthority();
            Status = PaymentStatus.Paid;
            CollectorId = null;
            ORNumber = null;
            PartialAmount = 0;
            Remarks = remarks;
            PaidAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            BumpSettlementVersion();
        }

        /// <summary>
        /// Records a PARTIAL online payment: the captured amount does NOT cover the current balance (e.g. the
        /// bill grew after the payor opened checkout). Marks Partial with the amount actually received so the
        /// under-payment is never silently cleared as fully Paid. Online carries no collector/OR (audited via
        /// remarks); delinquency then recomputes as arrears, not cleared.
        /// </summary>
        public void MarkPartiallyPaidOnline(decimal amountReceived, string remarks, string updatedBy = "Online")
        {
            EnsureLegacySettlementAuthority();
            Status = PaymentStatus.Partial;
            CollectorId = null;
            ORNumber = null;
            PartialAmount = amountReceived;
            Remarks = remarks;
            PaidAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            BumpSettlementVersion();
        }

        public void MarkUnpaid(string updatedBy = "System")
        {
            EnsureLegacySettlementAuthority();
            Status = PaymentStatus.Unpaid;
            ORNumber = null;
            CollectorId = null;
            PartialAmount = 0;
            PaidAt = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            BumpSettlementVersion();
        }

        /// <summary>Stamps the offline-sync idempotency key (set once when replaying a queued offline record).</summary>
        public void SetClientOperationId(Guid clientOperationId)
        {
            EnsureLegacySettlementAuthority();
            if (clientOperationId == Guid.Empty)
                throw new ArgumentException("Client operation id must be valid.", nameof(clientOperationId));
            ClientOperationId = clientOperationId;
        }

        public void MarkSettlementPendingCutover()
        {
            if (SettlementAuthorityState != SettlementAuthority.Legacy)
                throw new InvalidOperationException("Only a Legacy source can enter Pending Cutover.");
            SettlementAuthorityState = SettlementAuthority.PendingCutover;
            BumpSettlementVersion();
        }

        /// <summary>Advances the rent concurrency boundary while the source remains quiesced in Pending Cutover.</summary>
        public long AdvancePendingSettlementCutoverBoundary()
        {
            if (SettlementAuthorityState != SettlementAuthority.PendingCutover)
                throw new InvalidOperationException("Only a Pending Cutover rent source can freeze its opening position.");
            BumpSettlementVersion();
            return SettlementVersion;
        }

        public void ActivateCanonicalSettlement(CollectionSettlementCutover cutover)
        {
            ArgumentNullException.ThrowIfNull(cutover);
            if (SettlementAuthorityState != SettlementAuthority.PendingCutover)
                throw new InvalidOperationException("Reconciliation must complete before Canonical activation.");
            if (cutover.SourceKind != CollectionSourceKind.PaymentRecord
                || cutover.SourceId != Id || cutover.SourcePart is not null
                || (MunicipalityId != Guid.Empty && cutover.MunicipalityId != MunicipalityId))
                throw new InvalidOperationException("The frozen cutover does not identify this tenant's PaymentRecord source.");
            SettlementCutoverId = cutover.Id;
            SettlementAuthorityState = SettlementAuthority.Canonical;
            BumpSettlementVersion();
        }

        public void UpdateStatus(
            PaymentStatus status,
            decimal partialAmount = 0,
            string? remarks = null,
            string updatedBy = "System",
            Guid? collectorId = null)
        {
            EnsureLegacySettlementAuthority();
            // Auto-upgrade from Partial to Paid if partial amount equals or exceeds total bill
            if (status == PaymentStatus.Partial && partialAmount >= TotalBill)
            {
                status = PaymentStatus.Paid;
                partialAmount = 0; // Clear partial amount when fully paid
            }
            
            Status = status;
            PartialAmount = status == PaymentStatus.Partial ? partialAmount : 0;
            Remarks = remarks;
            PaidAt = status != PaymentStatus.Unpaid ? DateTime.UtcNow : null;
            if (status == PaymentStatus.Unpaid)
                ORNumber = null;
            else if (collectorId.HasValue)
                CollectorId = collectorId.Value;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            BumpSettlementVersion();
        }

        /// <summary>
        /// Projects canonical net rent allocations into the legacy status fields. Mixed historical components
        /// cannot be represented safely by the single legacy status and therefore require reconciliation.
        /// </summary>
        public void ApplyCanonicalRentProjection(decimal cumulativeRentSettled, DateTime recordedAtUtc, string updatedBy)
        {
            if (SettlementAuthorityState != SettlementAuthority.Canonical)
                throw new InvalidOperationException("Canonical rent projection requires Canonical settlement authority.");
            if (recordedAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Projection timestamp must be UTC.", nameof(recordedAtUtc));
            if (ElecAmount.GetValueOrDefault() != 0m || WaterAmount.GetValueOrDefault() != 0m
                || FishFeeAmount.GetValueOrDefault() != 0m)
                throw new InvalidOperationException("Mixed legacy components require reconciliation before rent projection.");
            if (cumulativeRentSettled < 0m || cumulativeRentSettled > BaseRentalAmount
                || decimal.Round(cumulativeRentSettled, 2, MidpointRounding.ToZero) != cumulativeRentSettled)
                throw new ArgumentOutOfRangeException(nameof(cumulativeRentSettled));

            if (cumulativeRentSettled >= BaseRentalAmount && BaseRentalAmount > 0m)
            {
                Status = PaymentStatus.Paid;
                PartialAmount = 0m;
                PaidAt = recordedAtUtc;
            }
            else if (cumulativeRentSettled > 0m)
            {
                Status = PaymentStatus.Partial;
                PartialAmount = cumulativeRentSettled;
                PaidAt = recordedAtUtc;
            }
            else
            {
                Status = PaymentStatus.Unpaid;
                PartialAmount = 0m;
                PaidAt = null;
            }

            UpdatedAt = recordedAtUtc;
            UpdatedBy = updatedBy;
            BumpSettlementVersion();
        }

        private void EnsureLegacySettlementAuthority()
        {
            if (SettlementAuthorityState != SettlementAuthority.Legacy)
                throw new InvalidOperationException("Legacy settlement writers are disabled once cutover begins.");
        }

        private void BumpSettlementVersion() => SettlementVersion = checked(SettlementVersion + 1);
    }
}
