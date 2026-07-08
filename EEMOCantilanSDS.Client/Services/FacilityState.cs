using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Per-circuit cache of the signed-in LGU's ACTUAL facilities, loaded once from
/// GET /api/facilities/summaries (tenant-scoped). Facility selectors/tabs should render from this instead
/// of the full <see cref="FacilityCode"/> enum, so an LGU never sees tabs for facilities it doesn't operate.
///
/// The list of facilities is data-driven per tenant. The rental-vs-transaction split is a platform constant:
/// the eight <see cref="FacilityCode"/> values have fixed billing conventions (NPM/TCC/NCC/BBQ/ICE are
/// recurring per-payor rentals; SLH/TRM/TPM are per-service transaction facilities). Only WHICH of these an
/// LGU has varies — and that comes from the API.
///
/// Fallback (before load / on failure) is the full set, so Cantilan (which has all eight) is byte-for-byte
/// unchanged and a transient failure never hides Cantilan's tabs.
/// </summary>
public class FacilityState(IFacilitiesApiClient api)
{
    private static readonly FacilityCode[] AllCodes =
        { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE, FacilityCode.SLH, FacilityCode.TRM, FacilityCode.TPM };

    private static readonly HashSet<FacilityCode> RentalCodes =
        new() { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE };

    private IReadOnlyList<FacilitySidebarSummaryDto>? _facilities;
    private Task? _loadTask;

    /// <summary>Loads the tenant's facilities once per circuit; concurrent callers share the in-flight task.</summary>
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var now = PhilippineTime.Now;
            var result = await api.GetFacilitySummariesAsync(now.Year, now.Month);
            if (result.IsSuccess && result.Value is not null)
                _facilities = result.Value;
        }
        catch
        {
            // Presentation-only; leave the fallback in place and never break the page.
        }
    }

    /// <summary>The LGU's facilities in canonical order (fallback = all eight until loaded).</summary>
    public IReadOnlyList<FacilityCode> All =>
        _facilities is { Count: > 0 }
            ? _facilities.Select(f => f.Code).OrderBy(c => (int)c).ToList()
            : AllCodes;

    /// <summary>The LGU's recurring-rental facilities (managed per-payor: collection, closed accounts, vendors).</summary>
    public IReadOnlyList<FacilityCode> Rental =>
        All.Where(RentalCodes.Contains).ToList();

    /// <summary>The LGU's per-service transaction facilities (SLH/TRM/TPM).</summary>
    public IReadOnlyList<FacilityCode> Transaction =>
        All.Where(c => !RentalCodes.Contains(c)).ToList();

    public static bool IsRental(FacilityCode code) => RentalCodes.Contains(code);

    /// <summary>The tenant's own name for a facility (fallback = the code).</summary>
    public string NameOf(FacilityCode code) =>
        _facilities?.FirstOrDefault(f => f.Code == code)?.Name ?? code.ToString();
}
