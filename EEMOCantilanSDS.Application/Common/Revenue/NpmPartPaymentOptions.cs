namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Whether NPM part payments may be quoted and recorded. Bound from <c>StallTrack:NpmPartPayments:Enabled</c> and OFF unless the
/// office switches it on, so the engine can ship and be reviewed before any municipality takes a part payment.
/// </summary>
public sealed record NpmPartPaymentOptions(bool Enabled = false, IReadOnlySet<Guid>? ApprovedContracts = null)
{
    public bool Allows(Guid contractId) => Enabled || contractId != Guid.Empty && ApprovedContracts?.Contains(contractId) == true;
}
