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
    DirectApprovedAmount = 2,

    /// <summary>
    /// The amount is the approved effective-dated rate of the vehicle class the collector states (Transportation / Parking,
    /// IA-030). The collector chooses a class from the approved list, never a rate.
    /// </summary>
    VehicleClassRate = 3
}
