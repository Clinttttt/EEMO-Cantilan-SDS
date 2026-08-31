using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetNpmMonthBasis;

/// <summary>
/// States the office's basis for a market month, and states it for its OWN market only.
/// </summary>
/// <remarks>
/// <para>
/// Nothing already collected is re-priced. The rule answers what a month owes when it is asked, so a period the office has
/// already worked keeps the figures it was worked at; what changes is what the next question is answered with. That is why
/// this writes a state rather than an effective-dated row: an office is on one basis, and its own audit trail records when
/// it changed.
/// </para>
/// <para>
/// The Head's decision. An Admin cannot set a rate, and this decides what every rate adds up to over a month.
/// </para>
/// </remarks>
public class SetNpmMonthBasisCommandHandler(
    IFacilityRepository facilityRepo,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext,
    IClock clock) : IRequestHandler<SetNpmMonthBasisCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetNpmMonthBasisCommand request, CancellationToken ct)
    {
        if (request.Basis is not (NpmMonthBasis.RentGoal or NpmMonthBasis.PureDays))
            return Result<bool>.Failure("That is not a basis this platform measures a month by.", ResultStatus.Invalid);

        var npm = await facilityRepo.GetByCodeAsync(FacilityCode.NPM, ct);
        if (npm is null) return Result<bool>.NotFound();

        npm.SetMonthBasis(request.Basis, "MonthBasis");
        await unitOfWork.SaveChangesAsync(ct);

        // Every figure a month adds up to is now answered differently: the ledger, the register, the reports and the
        // collector's own sheet all read the basis through the fee snapshot, so the views a payment affects are the views
        // this affects.
        var today = clock.PhilippineToday;
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, today.Year, today.Month, ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<bool>.Success(true);
    }
}
