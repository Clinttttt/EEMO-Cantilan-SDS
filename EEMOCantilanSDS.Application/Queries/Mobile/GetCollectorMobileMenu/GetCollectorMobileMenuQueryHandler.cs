using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;

public class GetCollectorMobileMenuQueryHandler(
    ICollectorRepository collectorRepository,
    IFacilityRepository facilityRepository,
    IMunicipalityRepository municipalityRepository,
    EEMOCantilanSDS.Application.Common.Tenancy.ITenantContext tenantContext,
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
        // scoped by the global query filter to the collector's OWN municipality.
        var names = await facilityRepository.GetFacilityNamesAsync(cancellationToken);

        // Only the facilities THIS municipality actually operates — never the full FacilityCode enum.
        // This drops unconfigured slots (e.g. Custom1–5) and other LGUs' facilities, and always uses the
        // tenant's own facility name (so a custom facility shows its real name, not "Custom1").
        // IsAssigned drives the lock; IsAvailable additionally requires a built mobile collection screen.
        var facilities = names
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var archetype = Facility.DefaultArchetypeFor(kv.Key);
                var isAssigned = assigned.Contains(kv.Key);
                return new MobileFacilityMenuItemDto(
                    kv.Key,
                    kv.Value,
                    GetFacilityDescription(kv.Key),
                    isAssigned,
                    // Available when assigned AND the billing archetype maps to a built mobile screen. Every
                    // archetype except the unmapped "Custom" has one (custom FACILITIES bill as MonthlyRental
                    // → the monthly screen), so a custom facility works end-to-end instead of being locked.
                    isAssigned && archetype != BillingArchetype.Custom,
                    archetype);
            })
            .ToList();

        return Result<MobileMenuDto>.Success(new MobileMenuDto(
            collector.Id,
            collector.FullName ?? "Collector",
            collector.EmployeeId ?? string.Empty,
            PhilippineTime.Today,
            facilities,
            await BuildBrandingAsync(cancellationToken)));
    }

    // The collector's LGU branding (seal/office/name) so the mobile header + receipts identify the correct
    // municipality. Best-effort: a missing record leaves branding null and the mobile keeps the Cantilan
    // defaults, so nothing breaks.
    private async Task<MobileBrandingDto?> BuildBrandingAsync(CancellationToken ct)
    {
        var m = await municipalityRepository.GetByIdentifierAsync(tenantContext.TenantCode, ct);
        return m is null
            ? null
            : new MobileBrandingDto(m.Name, m.Province, m.OfficeName, m.OfficeAcronym, m.SealPath);
    }

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
