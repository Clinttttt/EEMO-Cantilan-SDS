using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    /// <summary>
    /// A single-use, expiring code issued by staff so the person liable for a HISTORICAL account can see that debt in the Payor portal. It is bound to the
    /// original liable identity, never to a stall and never to a name: a stall-rent debt to the ORIGINAL <see cref="ContractId"/>, a space-rental debt to the
    /// <see cref="ObligationAccountId"/> (with the liable term's start). The code is tenant-bound (<see cref="IMunicipalityOwned"/>) and redeemable only with
    /// the contact number it was issued for. Redeeming it creates a <see cref="PayorLiabilityLink"/>; it never creates or merges a Payor, and it grants
    /// nothing on a successor's stall. Kept apart from <see cref="PayorActivationCode"/> so the current-occupant activation flow is untouched.
    /// </summary>
    public class PayorHistoricalAccessCode : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public string Code { get; private set; } = string.Empty;
        public string ContactNumber { get; private set; } = string.Empty;
        /// <summary>The liable party's name as the office holds it, copied at issuance so activation never asks the payor to type one.</summary>
        public string PayerName { get; private set; } = string.Empty;
        public Guid? ContractId { get; private set; }
        public Guid? ObligationAccountId { get; private set; }
        public DateOnly? TermStart { get; private set; }
        public DateTime ExpiresAt { get; private set; }
        public bool IsUsed { get; private set; }
        public DateTime? UsedAt { get; private set; }
        public Guid? PayorUserId { get; private set; }

        private PayorHistoricalAccessCode() { }

        public static PayorHistoricalAccessCode Create(string code, string contactNumber, string payerName, Guid? contractId, Guid? obligationAccountId,
            DateOnly? termStart, DateTime expiresAt, string createdBy)
        {
            if (contractId.HasValue == obligationAccountId.HasValue)
                throw new ArgumentException("A historical access code is bound to exactly one liable identity: a contract or an obligation account.");
            return new PayorHistoricalAccessCode
            {
                Id = Guid.NewGuid(), Code = code, ContactNumber = contactNumber, PayerName = payerName, ContractId = contractId,
                ObligationAccountId = obligationAccountId, TermStart = termStart, ExpiresAt = expiresAt, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy
            };
        }

        public bool CanBeRedeemedBy(string contactNumber) =>
            !IsUsed && ExpiresAt > DateTime.UtcNow && string.Equals(ContactNumber, contactNumber, StringComparison.Ordinal);

        public void MarkUsed(Guid payorUserId)
        {
            IsUsed = true; UsedAt = DateTime.UtcNow; PayorUserId = payorUserId; UpdatedAt = DateTime.UtcNow; UpdatedBy = "Self-Activation";
        }
    }
}
