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

        // Fee breakdown
        public decimal BaseRentalAmount { get; private set; }
        public decimal PartialAmount { get; private set; }

        // Utilities
        public decimal? ElecReading { get; private set; }
        public decimal? ElecAmount { get; private set; }
        public decimal? WaterReading { get; private set; }
        public decimal? WaterAmount { get; private set; }

        // Fish fee — NPM Fish Section only (₱1/kg)
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

        public void UpdateStatus(
            PaymentStatus status,
            decimal partialAmount = 0,
            string? remarks = null,
            string updatedBy = "System")
        {
            Status = status;
            PartialAmount = status == PaymentStatus.Partial ? partialAmount : 0;
            Remarks = remarks;
            PaidAt = status != PaymentStatus.Unpaid ? DateTime.UtcNow : null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
