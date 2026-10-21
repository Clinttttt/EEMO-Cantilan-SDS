using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;

/// <summary>
/// Facilities billed as a flat monthly rental (one PaymentRecord per stall per month).
/// NPM is daily-collected unless its LGU declared standard monthly rental (an NPM facility whose stored archetype is
/// MonthlyRental); SLH/TRM/TPM are per-transaction — none belong here.
/// </summary>
public static class MonthlyRentalFacilities
{
    /// <summary>True when a facility with this code and stored archetype is billed as a flat monthly rental.</summary>
    public static bool Bills(FacilityCode code, BillingArchetype archetype) =>
        Codes.Contains(code) || (code == FacilityCode.NPM && archetype == BillingArchetype.MonthlyRental);

    public static bool Bills(EEMOCantilanSDS.Domain.Entities.Facilities.Facility facility) => Bills(facility.Code, facility.Archetype);

    /// <summary>EF-translatable form of <see cref="Bills(FacilityCode, BillingArchetype)"/>.</summary>
    public static readonly System.Linq.Expressions.Expression<Func<EEMOCantilanSDS.Domain.Entities.Facilities.Facility, bool>> BillsMonthly =
        f => f.Code == FacilityCode.TCC || f.Code == FacilityCode.NCC || f.Code == FacilityCode.BBQ || f.Code == FacilityCode.ICE
            || f.Code == FacilityCode.Custom1 || f.Code == FacilityCode.Custom2 || f.Code == FacilityCode.Custom3
            || f.Code == FacilityCode.Custom4 || f.Code == FacilityCode.Custom5
            || (f.Code == FacilityCode.NPM && f.Archetype == BillingArchetype.MonthlyRental);

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
