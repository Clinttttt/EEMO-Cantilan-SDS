using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    public class PaymentRecord : AuditableEntity
    {
        public Guid StallId { get; private set; }
        public Guid? CollectorId { get; private set; }
        public int BillingYear { get; private set; }
        public int BillingMonth { get; private set; }
        public PaymentStatus Status { get; private set; } = PaymentStatus.Unpaid;
        public string? ORNumber { get; private set; }
        public DateTime? PaidAt { get; private set; }

        // Offline-sync idempotency key from the mobile client (null for online records).
        public Guid? ClientOperationId { get; private set; }

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
        }
        /// <summary>
        /// Attaches a manually-entered OR number to an already-recorded payment without
        /// altering the fee breakdown, status, or original collector attribution.
        /// </summary>
        public void SetOrNumber(string orNumber, string updatedBy = "System")
        {
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
            Status = PaymentStatus.Paid;
            CollectorId = null;
            ORNumber = null;
            PartialAmount = 0;
            Remarks = remarks;
            PaidAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void MarkUnpaid(string updatedBy = "System")
        {
            Status = PaymentStatus.Unpaid;
            ORNumber = null;
            CollectorId = null;
            PartialAmount = 0;
            PaidAt = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>Stamps the offline-sync idempotency key (set once when replaying a queued offline record).</summary>
        public void SetClientOperationId(Guid clientOperationId) => ClientOperationId = clientOperationId;

        public void UpdateStatus(
            PaymentStatus status,
            decimal partialAmount = 0,
            string? remarks = null,
            string updatedBy = "System",
            Guid? collectorId = null)
        {
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
        }
    }
}
