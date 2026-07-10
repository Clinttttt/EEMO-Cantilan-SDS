using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityConfiguration;

public class GetFacilityConfigurationQueryHandler(IFacilityRepository facilityRepository)
    : IRequestHandler<GetFacilityConfigurationQuery, Result<FacilityConfigurationDto>>
{
    public async Task<Result<FacilityConfigurationDto>> Handle(GetFacilityConfigurationQuery request, CancellationToken ct)
    {
        // Configured facilities for the caller's tenant (query filter scopes to the current LGU).
        var configured = await facilityRepository.GetConfiguredFacilitiesAsync(ct);

        // Available = the canonical types this tenant has NOT configured yet.
        var configuredCodes = configured
            .Select(c => c.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var available = FacilityCatalog.AllCodes
            .Where(code => !configuredCodes.Contains(code.ToString()))
            .Select(code =>
            {
                var (name, shortName) = FacilityCatalog.Defaults(code);
                return new AvailableFacilityDto(
                    code.ToString(),
                    name,
                    shortName,
                    FacilityDisplay.BillingModel(Facility.DefaultArchetypeFor(code)));
            })
            .ToList();

        // Offer one Head-named custom (monthly-rental) facility if a reserved slot is still free.
        var nextCustom = FacilityCatalog.CustomCodes
            .Cast<FacilityCode?>()
            .FirstOrDefault(c => !configuredCodes.Contains(c!.Value.ToString()));
        if (nextCustom is not null)
        {
            available.Add(new AvailableFacilityDto(
                nextCustom.Value.ToString(),
                "Custom facility",
                string.Empty,
                "Monthly rental",
                IsCustom: true));
        }

        return Result<FacilityConfigurationDto>.Success(new FacilityConfigurationDto(configured, available));
    }
}
