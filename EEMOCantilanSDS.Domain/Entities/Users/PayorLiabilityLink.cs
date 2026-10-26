using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    /// <summary>
    /// Explicit evidence that a <see cref="PayorUser"/> is the person liable for ONE historical account: the original stall <see cref="ContractId"/> or the
    /// space <see cref="ObligationAccountId"/>. Created only by redeeming a <see cref="PayorHistoricalAccessCode"/> the office issued for that exact
    /// debtor. It is read-only visibility of that account's own unpaid obligations; it is not a stall link, authorizes no online payment and is never
    /// derived from a name, number or spelling.
    /// </summary>
    public class PayorLiabilityLink : BaseEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public Guid PayorUserId { get; private set; }
        public Guid? ContractId { get; private set; }
        public Guid? ObligationAccountId { get; private set; }
        public DateOnly? TermStart { get; private set; }
        public Guid SourceCodeId { get; private set; }
        public DateTime LinkedAt { get; private set; }
        public string LinkedBy { get; private set; } = string.Empty;

        private PayorLiabilityLink() { }

        public static PayorLiabilityLink Create(Guid payorUserId, PayorHistoricalAccessCode code) => new()
        {
            Id = Guid.NewGuid(), PayorUserId = payorUserId, ContractId = code.ContractId, ObligationAccountId = code.ObligationAccountId,
            TermStart = code.TermStart, SourceCodeId = code.Id, LinkedAt = DateTime.UtcNow, LinkedBy = code.CreatedBy ?? "Staff"
        };
    }
}
