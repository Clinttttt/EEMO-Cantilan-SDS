using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.AddNpmCustomSection;

public class AddNpmCustomSectionCommandHandler(
    IFacilityRepository facilityRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext,
    IAppDbContext context,
    IClock clock) : IRequestHandler<AddNpmCustomSectionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(AddNpmCustomSectionCommand request, CancellationToken ct)
    {
        var npm = await facilityRepo.GetByCodeAsync(FacilityCode.NPM, ct);
        if (npm is null) return Result<bool>.NotFound();

        if (request.DailyRate is { } rate && rate < 0m)
            return Result<bool>.Failure("A daily fee cannot be negative.", ResultStatus.Invalid);

        // Idempotent: AddCustomSection is a no-op if the (trimmed, case-insensitive) name already exists.
        npm.AddCustomSection(request.Name, currentUser.Username ?? "Admin");

        // The office may price the section as it creates it. Written under the name now registered, effective TODAY and
        // never backwards, so a section created today prices only today onward. A section left unpriced has its stalls
        // billed the market's own rate, exactly as before this was possible.
        if (request.DailyRate is { } dailyRate && dailyRate > 0m)
        {
            var name = request.Name.Trim();
            var registered = npm.CustomSectionNames
                .FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;

            var effective = clock.PhilippineToday;
            var already = await context.FacilitySectionRates
                .FirstOrDefaultAsync(r => r.FacilityCode == FacilityCode.NPM
                                       && r.SectionName == registered
                                       && r.EffectiveDate == effective, ct);

            if (already is not null)
                already.UpdateAmount(dailyRate, currentUser.Username ?? "Admin");
            else
                context.FacilitySectionRates.Add(FacilitySectionRate.Create(
                    FacilityCode.NPM, registered, dailyRate, effective, createdBy: currentUser.Username ?? "Admin"));
        }

        await uow.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);
        return Result<bool>.Success(true);
    }
}
