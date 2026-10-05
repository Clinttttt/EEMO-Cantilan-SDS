namespace EEMOCantilanSDS.Domain.Constants;

/// <summary>IA-064 prospective decision. Earlier obligation records remain historical evidence.</summary>
public static class FishMeatVendorFeeRules
{
    public static readonly DateOnly DirectEffectiveDate = new(2026, 10, 5);
    public static bool UsesDirectCollection(DateOnly businessDate) => businessDate >= DirectEffectiveDate;
}
