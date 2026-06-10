using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;

public class GetCollectorMobileMenuQueryHandler(
    ICollectorRepository collectorRepository,
    IFacilityRepository facilityRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetCollectorMobileMenuQuery, Result<MobileMenuDto>>
{
    public async Task<Result<MobileMenuDto>> Handle(GetCollectorMobileMenuQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileMenuDto>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, cancellationToken);
        if (collector is null)
            return Result<MobileMenuDto>.NotFound();

        var assigned = collector.FacilityAssignments
            .Select(a => a.FacilityCode)
            .ToHashSet();

        // Facility display names come from the seeded Facility records (single source of truth),
        // falling back to the code if a facility row is missing.
        var names = await facilityRepository.GetFacilityNamesAsync(cancellationToken);

        // Show every facility so the collector sees what they can and cannot access.
        // IsAssigned drives the lock; IsAvailable additionally requires a built mobile
        // collection screen (flip a code on here as each facility's page ships).
        var facilities = Enum.GetValues<FacilityCode>()
            .OrderBy(code => code)
            .Select(code => new MobileFacilityMenuItemDto(
                code,
                names.TryGetValue(code, out var name) ? name : code.ToString(),
                GetFacilityDescription(code),
                assigned.Contains(code),
                assigned.Contains(code) && ImplementedMobileFacilities.Contains(code)))
            .ToList();

        return Result<MobileMenuDto>.Success(new MobileMenuDto(
            collector.Id,
            collector.FullName ?? "Collector",
            collector.EmployeeId ?? string.Empty,
            PhilippineTime.Today,
            facilities));
    }

    /// <summary>
    /// Facilities whose mobile collection screen is implemented and ready to open.
    /// Add a code here when its page + endpoints ship; the menu opens it automatically.
    /// </summary>
    private static readonly HashSet<FacilityCode> ImplementedMobileFacilities = new()
    {
        FacilityCode.NPM,
        FacilityCode.TCC,
        FacilityCode.NCC,
        FacilityCode.BBQ,
        FacilityCode.ICE,
        FacilityCode.SLH,
        FacilityCode.TRM,
        FacilityCode.TPM
    };

    private static string GetFacilityDescription(FacilityCode code) => code switch
    {
        FacilityCode.NPM => "NPM - Stall Rental & Fees",
        FacilityCode.TCC => "TCC - Commercial Stall Rental",
        FacilityCode.NCC => "NCC - Commercial Unit Rental",
        FacilityCode.BBQ => "BBQ - Stand Fees",
        FacilityCode.ICE => "ICE - Ice Plant Collections",
        FacilityCode.SLH => "SLH - Slaughter & Inspection Fees",
        FacilityCode.TRM => "TRM - Terminal & Parking Fees",
        FacilityCode.TPM => "TPM - Tabo-an Market Fees",
        _ => "Assigned facility"
    };
}
