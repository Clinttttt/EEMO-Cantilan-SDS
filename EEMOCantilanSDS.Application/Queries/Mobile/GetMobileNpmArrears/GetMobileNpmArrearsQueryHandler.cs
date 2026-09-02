using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmArrears;

/// <summary>
/// Answers the arrears screen for the collector who asked, on exactly the terms the daily round is authorised.
/// </summary>
/// <remarks>
/// The same three checks the round makes, in the same order: a collector, one the office still has on file, and one assigned to
/// this market. Arrears state what every payor of the market owes, so an unassigned collector must not be able to read it by
/// opening a different screen.
/// </remarks>
public sealed class GetMobileNpmArrearsQueryHandler(
    ICollectorRepository collectorRepository,
    IStallMobileQueries mobileQueries,
    ICurrentUserService currentUser, IClock clock) : IRequestHandler<GetMobileNpmArrearsQuery, Result<MobileNpmArrearsDto>>
{
    public async Task<Result<MobileNpmArrearsDto>> Handle(GetMobileNpmArrearsQuery request, CancellationToken ct)
    {
        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileNpmArrearsDto>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, ct);
        if (collector is null)
            return Result<MobileNpmArrearsDto>.NotFound();

        var hasNpmAssignment = collector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM);
        if (!hasNpmAssignment)
            return Result<MobileNpmArrearsDto>.Forbidden();

        var arrears = await mobileQueries.GetMobileNpmArrearsAsync(
            request.Year,
            request.Month,
            clock.PhilippineToday,
            ct);

        return Result<MobileNpmArrearsDto>.Success(arrears);
    }
}
