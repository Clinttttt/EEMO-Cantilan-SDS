using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetNpmSectionUtilities;

/// <summary>
/// Writes the one row that holds a section's metering default: found by section and set, or added. No history, because a
/// default bills nothing and there is nothing to reconcile against later.
/// </summary>
public class SetNpmSectionUtilitiesCommandHandler(
    IAppDbContext context,
    IFacilityRepository facilityRepo,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SetNpmSectionUtilitiesCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetNpmSectionUtilitiesCommand request, CancellationToken ct)
    {
        var section = (request.Section ?? string.Empty).Trim();
        if (section.Length == 0)
            return Result<bool>.Failure("Name the section these meters belong to.", ResultStatus.Invalid);

        // Only for a section the office has registered, and under the name it registered — the same guard the section's
        // fee has, so a typo cannot leave a row describing a section that does not exist.
        var npm = await facilityRepo.GetByCodeAsync(FacilityCode.NPM, ct);
        if (npm is null) return Result<bool>.NotFound();

        var registered = npm.CustomSectionNames
            .FirstOrDefault(n => string.Equals(n, section, StringComparison.OrdinalIgnoreCase));
        if (registered is null)
            return Result<bool>.Failure($"{section} is not one of your market's sections.", ResultStatus.Invalid);

        var existing = await context.FacilitySectionUtilities
            .FirstOrDefaultAsync(u => u.FacilityCode == FacilityCode.NPM && u.SectionName == registered, ct);

        if (existing is not null)
            existing.Set(request.Electricity, request.Water, "SectionUtilities");
        else
            context.FacilitySectionUtilities.Add(FacilitySectionUtilities.Create(
                FacilityCode.NPM, registered, request.Electricity, request.Water, createdBy: "SectionUtilities"));

        await context.SaveChangesAsync(ct);

        // Reference data only: the stall form reads this, and nothing already billed changes.
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<bool>.Success(true);
    }
}
