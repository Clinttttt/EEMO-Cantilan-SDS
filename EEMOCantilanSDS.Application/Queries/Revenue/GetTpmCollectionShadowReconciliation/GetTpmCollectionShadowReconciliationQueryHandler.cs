using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetTpmCollectionShadowReconciliation;

public sealed class GetTpmCollectionShadowReconciliationQueryHandler(
    IAppDbContext context,
    ICurrentUserService currentUser)
    : IRequestHandler<GetTpmCollectionShadowReconciliationQuery, Result<TpmCollectionShadowReconciliationDto>>
{
    public async Task<Result<TpmCollectionShadowReconciliationDto>> Handle(
        GetTpmCollectionShadowReconciliationQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.MunicipalityId is not { } municipalityId
            || municipalityId == Guid.Empty)
            return Result<TpmCollectionShadowReconciliationDto>.Forbidden();

        var attendances = await context.TpmAttendances
            .AsNoTracking()
            .Where(x => x.MunicipalityId == municipalityId
                && x.IsPaid
                && x.MarketDate >= request.From
                && x.MarketDate <= request.To)
            .OrderBy(x => x.MarketDate)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                BusinessDate = x.MarketDate,
                Amount = x.Fee,
                x.CollectorId
            })
            .ToListAsync(cancellationToken);

        var sourceTotal = attendances.Sum(x => x.Amount);
        var projectedRows = new List<TpmCollectionShadowProjectedRowDto>();
        var unresolvedRows = new List<TpmCollectionShadowUnresolvedRowDto>();

        if (attendances.Count == 0)
        {
            return Result<TpmCollectionShadowReconciliationDto>.Success(
                new(request.From, request.To, 0, 0m, projectedRows, unresolvedRows));
        }

        var taboClassification = await context.RevenueClassifications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.MunicipalityId == municipalityId
                    && x.SemanticCode == RevenueClassificationCodes.Tabo,
                cancellationToken);

        if (taboClassification is null)
        {
            unresolvedRows.AddRange(attendances.Select(x => new TpmCollectionShadowUnresolvedRowDto(
                x.Id,
                x.BusinessDate,
                x.Amount,
                TpmCollectionShadowUnresolvedReasons.TaboClassificationMissingCode,
                TpmCollectionShadowUnresolvedReasons.TaboClassificationMissingMessage)));
        }
        else
        {
            // Historical reconciliation intentionally ignores current IsActive. Retirement must not erase
            // the classification identity that was valid for money received on an earlier business date.
            var policies = await context.RevenueClassificationPolicies
                .AsNoTracking()
                .Where(x => x.MunicipalityId == municipalityId
                    && x.RevenueClassificationId == taboClassification.Id
                    && x.EffectiveDate <= request.To)
                .OrderByDescending(x => x.EffectiveDate)
                .ThenByDescending(x => x.Id)
                .Select(x => new { x.Id, x.EffectiveDate })
                .ToListAsync(cancellationToken);

            foreach (var attendance in attendances)
            {
                var policy = policies.FirstOrDefault(x => x.EffectiveDate <= attendance.BusinessDate);
                if (policy is null)
                {
                    unresolvedRows.Add(new TpmCollectionShadowUnresolvedRowDto(
                        attendance.Id,
                        attendance.BusinessDate,
                        attendance.Amount,
                        TpmCollectionShadowUnresolvedReasons.TaboPolicyNotEffectiveCode,
                        TpmCollectionShadowUnresolvedReasons.TaboPolicyNotEffectiveMessage));
                    continue;
                }

                projectedRows.Add(new TpmCollectionShadowProjectedRowDto(
                    attendance.Id,
                    attendance.BusinessDate,
                    attendance.Amount,
                    attendance.CollectorId,
                    taboClassification.Id,
                    policy.Id,
                    policy.EffectiveDate,
                    CollectionSourceKind.TpmAttendance,
                    attendance.Id));
            }
        }

        return Result<TpmCollectionShadowReconciliationDto>.Success(
            new(
                request.From,
                request.To,
                attendances.Count,
                sourceTotal,
                projectedRows,
                unresolvedRows));
    }
}
