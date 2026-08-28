using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetNpmSectionRate;

/// <summary>
/// Writes one effective-dated section rate, mirroring <c>SetFacilityRate</c> so a section's fee is set by the same rule
/// every other rate here is: today forward, and an edit landing on today's row adjusts it rather than adding a second.
/// </summary>
public class SetNpmSectionRateCommandHandler(
    IAppDbContext context,
    IFacilityRepository facilityRepo,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext,
    IClock clock) : IRequestHandler<SetNpmSectionRateCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetNpmSectionRateCommand request, CancellationToken ct)
    {
        var section = (request.Section ?? string.Empty).Trim();
        if (section.Length == 0)
            return Result<bool>.Failure("Name the section whose fee is being set.", ResultStatus.Invalid);

        if (request.DailyRate < 0m)
            return Result<bool>.Failure("A daily fee cannot be negative.", ResultStatus.Invalid);

        // Priced only for a section the office has actually registered, and under the name it registered. Otherwise a
        // typo would create a rate row that prices nothing, sitting in the office's own records for ever.
        var npm = await facilityRepo.GetByCodeAsync(FacilityCode.NPM, ct);
        if (npm is null) return Result<bool>.NotFound();

        var registered = npm.CustomSectionNames
            .FirstOrDefault(n => string.Equals(n, section, StringComparison.OrdinalIgnoreCase));
        if (registered is null)
            return Result<bool>.Failure($"{section} is not one of your market's sections.", ResultStatus.Invalid);

        // Effective today forward — never retroactive, so elapsed days stay exactly as billed.
        var effective = clock.PhilippineToday;

        var existing = await context.FacilitySectionRates
            .FirstOrDefaultAsync(r => r.FacilityCode == FacilityCode.NPM
                                   && r.SectionName == registered
                                   && r.EffectiveDate == effective, ct);

        if (existing is not null)
            existing.UpdateAmount(request.DailyRate, "SectionRateEdit");
        else
            context.FacilitySectionRates.Add(FacilitySectionRate.Create(
                FacilityCode.NPM, registered, request.DailyRate, effective, createdBy: "SectionRateEdit"));

        await context.SaveChangesAsync(ct);

        // The market's current-period views and the stallholder roster both derive money from the fee snapshot.
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, effective.Year, effective.Month, ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<bool>.Success(true);
    }
}
