using System;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    /// <summary>
    /// One receipt toward a monthly rent record paid in more than one receipt (IA-081): its amount, when it came in, its OR and its
    /// collector. Kept from the moment a month that already holds money receives more, so finishing a partial month never moves the
    /// first receipt's money into the finishing month or overwrites its OR, date and collector. The receipts always add up to the
    /// record's amount paid: a correction lowers the newest first and never deletes one, so an issued OR stays recorded.
    /// </summary>
    public class PaymentInstalment : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public Guid PaymentRecordId { get; private set; }
        public decimal Amount { get; private set; }
        public DateTime ReceivedAt { get; private set; }
        public string? ORNumber { get; private set; }
        public Guid? CollectorId { get; private set; }

        private PaymentInstalment() { }

        internal static PaymentInstalment Create(Guid paymentRecordId, Guid municipalityId, decimal amount, DateTime receivedAt,
            string? orNumber, Guid? collectorId, string createdBy) => new()
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            PaymentRecordId = paymentRecordId,
            Amount = amount,
            ReceivedAt = receivedAt,
            ORNumber = orNumber,
            CollectorId = collectorId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        /// <summary>Lowers this receipt by up to <paramref name="by"/> and returns how much it was lowered.</summary>
        internal decimal Reduce(decimal by, string updatedBy)
        {
            var cut = Math.Min(by, Amount);
            Amount -= cut;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            return cut;
        }

        internal void SetOrNumber(string orNumber, string updatedBy)
        {
            ORNumber = orNumber;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
