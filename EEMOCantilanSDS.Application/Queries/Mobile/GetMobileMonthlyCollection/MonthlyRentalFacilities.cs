using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;

/// <summary>
/// Facilities billed as a flat monthly rental (one PaymentRecord per stall per month).
/// NPM is daily-collected; SLH/TRM/TPM are per-transaction — none belong here.
/// </summary>
public static class MonthlyRentalFacilities
{
    public static readonly IReadOnlySet<FacilityCode> Codes = new HashSet<FacilityCode>
    {
        FacilityCode.TCC,
        FacilityCode.NCC,
        FacilityCode.BBQ,
        FacilityCode.ICE
    };
}
