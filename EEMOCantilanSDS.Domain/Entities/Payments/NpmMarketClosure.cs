using System;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    /// <summary>
    /// A facility-wide closure of the New Public Market for a single day — every NPM payor is excused
    /// for that date (₱0 owed, never unpaid/missed). It is independent of per-stall
    /// <see cref="DailyCollection.IsAbsent"/>: a closure excuses ALL stalls at once. Clearing a closure
    /// hard-deletes the row (audited as a delete).
    /// </summary>
    public class NpmMarketClosure : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public DateOnly ClosureDate { get; private set; }
        public MarketClosureReason Reason { get; private set; }
        public string? Remarks { get; private set; }

        private NpmMarketClosure() { }

        public static NpmMarketClosure Create(
            DateOnly closureDate,
            MarketClosureReason reason = MarketClosureReason.ApprovedByEemo,
            string? remarks = null,
            string createdBy = "System")
        {
            return new NpmMarketClosure
            {
                Id = Guid.NewGuid(),
                ClosureDate = closureDate,
                Reason = reason,
                Remarks = remarks,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        public void Update(MarketClosureReason reason, string? remarks, string updatedBy)
        {
            Reason = reason;
            Remarks = remarks;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
