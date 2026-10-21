using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Explicit rollout gate for the new office shortcut; existing per-row cutover remains mandatory.</summary>
public sealed record MonthlyRentReceiptOptions(bool OfficeEnabled = false,
    IReadOnlySet<FacilityCode>? EnabledFacilities = null, IReadOnlySet<Guid>? AcceptedMunicipalities = null)
{
    public bool Allows(Guid tenant, FacilityCode facility) => OfficeEnabled && tenant != Guid.Empty
        && facility is FacilityCode.NCC or FacilityCode.TCC or FacilityCode.BBQ or FacilityCode.ICE or FacilityCode.NPM
        && EnabledFacilities?.Contains(facility) == true && AcceptedMunicipalities?.Contains(tenant) == true;
}
