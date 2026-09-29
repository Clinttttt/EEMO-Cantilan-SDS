namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// Business context used to resolve an accountable-instrument policy.
/// Default preserves the established single-policy stream; specialized contexts are explicit and finite.
/// </summary>
public enum RevenuePolicyContext
{
    Default = 0,
    VegetableWholePayment = 1,
    VegetableDailyTransaction = 2
}
