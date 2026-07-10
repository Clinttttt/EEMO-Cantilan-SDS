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
        FacilityCode.ICE,
        // Custom facilities are monthly-rental too (one PaymentRecord per stall per month).
        FacilityCode.Custom1,
        FacilityCode.Custom2,
        FacilityCode.Custom3,
        FacilityCode.Custom4,
        FacilityCode.Custom5,
    };
}
