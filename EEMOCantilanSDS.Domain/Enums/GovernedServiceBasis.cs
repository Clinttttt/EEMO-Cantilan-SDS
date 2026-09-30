namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// How a governed configurable service arrives at the amount of one transaction. The basis is approved tenant
/// configuration; a collector never chooses it and never defines a rate.
/// </summary>
public enum GovernedServiceBasis
{
    /// <summary>One approved amount; the transaction must equal it.</summary>
    FixedAmount = 1,

    /// <summary>
    /// The collector records the amount actually charged, but only for a service whose approved definition enables
    /// this, and never above the approved ceiling when one is configured.
    /// </summary>
    DirectApprovedAmount = 2
}
