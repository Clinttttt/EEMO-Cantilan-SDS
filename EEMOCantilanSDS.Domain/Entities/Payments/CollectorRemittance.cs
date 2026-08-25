using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    /// <summary>
    /// Cash a collector has turned over to the office, and the days of collection it answers for.
    ///
    /// <para>
    /// This is a record of CUSTODY, not of what a payor owes. Nothing here changes a fee, a balance, a collection rate or
    /// any facility report: those state what was collected, this states what has since been handed in. The office asked for
    /// it because the system could say what a collector took and never what they remitted.
    /// </para>
    ///
    /// <para>
    /// A remittance covers a RANGE OF COLLECTION DAYS rather than a list of receipts, at the office's instruction: the
    /// counter stays quick and the arithmetic still holds, because two remittances of one collector may not overlap. That
    /// single rule is what makes "not yet remitted" an exact figure instead of a running guess. The days are matched on when
    /// the money was taken, never on the day a fee was for, or a payor settling an owed day would leave money that could
    /// never be remitted.
    /// </para>
    ///
    /// <para>
    /// Utility collections (electricity and water) are banked separately as additional income and are therefore outside
    /// this record and outside the figure it is checked against.
    /// </para>
    ///
    /// <para>
    /// It is never deleted. A mistake is VOIDED with a reason, which keeps the money trail readable.
    /// </para>
    /// </summary>
    public class CollectorRemittance : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        /// <summary>The accountable officer who handed the money in.</summary>
        public Guid CollectorId { get; private set; }

        public decimal Amount { get; private set; }

        /// <summary>When the office received it. Defaults to the moment it is recorded and may be corrected.</summary>
        public DateTime ReceivedAt { get; private set; }

        /// <summary>First and last day of collection this remittance answers for, inclusive.</summary>
        public DateOnly CoversFrom { get; private set; }

        public DateOnly CoversTo { get; private set; }

        /// <summary>
        /// The office account that received the money: the Head or an Administrator, who are the ones accountable on the
        /// portal. The name is kept as written at the time, so a reprint of an old document does not silently change hands
        /// because an account was later renamed or removed.
        /// </summary>
        public Guid ReceivedById { get; private set; }

        public string ReceivedByName { get; private set; } = string.Empty;

        /// <summary>Report of collections or deposit slip number. Optional, and the office is warned when it is left out.</summary>
        public string? ReferenceNo { get; private set; }

        public string? Notes { get; private set; }

        /// <summary>Why the remittance was voided. Set only alongside <see cref="AuditableEntity.IsDeleted"/>.</summary>
        public string? VoidReason { get; private set; }

        public Users.CollectorUser? Collector { get; private set; }

        private CollectorRemittance() { }

        public static CollectorRemittance Create(
            Guid collectorId,
            decimal amount,
            DateTime receivedAtUtc,
            DateOnly coversFrom,
            DateOnly coversTo,
            Guid receivedById,
            string receivedByName,
            string? referenceNo,
            string? notes,
            string createdBy)
        {
            if (amount <= 0m)
                throw new ArgumentOutOfRangeException(nameof(amount), "A remittance is money handed over, so it is more than zero.");
            if (coversTo < coversFrom)
                throw new ArgumentException("The covered period ends before it begins.", nameof(coversTo));

            return new CollectorRemittance
            {
                Id = Guid.NewGuid(),
                CollectorId = collectorId,
                Amount = amount,
                ReceivedAt = receivedAtUtc,
                CoversFrom = coversFrom,
                CoversTo = coversTo,
                ReceivedById = receivedById,
                ReceivedByName = Trim(receivedByName, 120) ?? string.Empty,
                ReferenceNo = Trim(referenceNo, 60),
                Notes = Trim(notes, 400),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        /// <summary>Corrects a recorded remittance. The previous values stay in the audit trail.</summary>
        public void Amend(
            decimal amount,
            DateTime receivedAtUtc,
            DateOnly coversFrom,
            DateOnly coversTo,
            string? referenceNo,
            string? notes,
            string updatedBy)
        {
            if (amount <= 0m)
                throw new ArgumentOutOfRangeException(nameof(amount), "A remittance is money handed over, so it is more than zero.");
            if (coversTo < coversFrom)
                throw new ArgumentException("The covered period ends before it begins.", nameof(coversTo));

            Amount = amount;
            ReceivedAt = receivedAtUtc;
            CoversFrom = coversFrom;
            CoversTo = coversTo;
            ReferenceNo = Trim(referenceNo, 60);
            Notes = Trim(notes, 400);
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>
        /// Withdraws a remittance recorded in error, keeping it on the record with the reason. The covered days are freed,
        /// so the correct remittance can then be recorded over the same period.
        /// </summary>
        public void Void(string reason, string voidedBy)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A voided remittance has to say why.", nameof(reason));

            VoidReason = Trim(reason, 400);
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = voidedBy;
            SoftDelete(voidedBy);
        }

        private static string? Trim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }
    }
}
