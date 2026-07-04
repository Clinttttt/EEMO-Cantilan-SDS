using System;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    /// <summary>
    /// An admin-approved excused month for a monthly-rental stall (TCC / NCC / BBQ / ICE). The stall
    /// owes ₱0 for this billing month, and the month never counts as unpaid / missed / delinquent.
    /// NPM (daily collection) uses per-day <see cref="DailyCollection.IsAbsent"/> instead; this is the
    /// monthly-facility equivalent. Clearing an exception hard-deletes the row (audited as a delete).
    /// </summary>
    public class StallMonthlyException : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public Guid StallId { get; private set; }
        public int BillingYear { get; private set; }
        public int BillingMonth { get; private set; }
        public MonthlyExceptionReason Reason { get; private set; }
        public string? Remarks { get; private set; }
        public Facilities.Stall? Stall { get; private set; }

        private StallMonthlyException() { }

        public static StallMonthlyException Create(
            Guid stallId,
            int billingYear,
            int billingMonth,
            MonthlyExceptionReason reason = MonthlyExceptionReason.ApprovedByEemo,
            string? remarks = null,
            string createdBy = "System")
        {
            return new StallMonthlyException
            {
                Id = Guid.NewGuid(),
                StallId = stallId,
                BillingYear = billingYear,
                BillingMonth = billingMonth,
                Reason = reason,
                Remarks = remarks,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        public void Update(MonthlyExceptionReason reason, string? remarks, string updatedBy)
        {
            Reason = reason;
            Remarks = remarks;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
