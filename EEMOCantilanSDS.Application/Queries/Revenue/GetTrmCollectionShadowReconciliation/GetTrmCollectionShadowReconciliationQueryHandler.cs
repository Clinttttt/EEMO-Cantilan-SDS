using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetTrmCollectionShadowReconciliation;

/// <summary>
/// Read-only shadow projection of authoritative legacy TRM trips. It never creates or modifies ledger rows.
/// </summary>
public sealed class GetTrmCollectionShadowReconciliationQueryHandler(
    IAppDbContext context,
    ICurrentUserService currentUser)
    : IRequestHandler<GetTrmCollectionShadowReconciliationQuery, Result<TrmCollectionShadowReconciliationDto>>
{
    public async Task<Result<TrmCollectionShadowReconciliationDto>> Handle(
        GetTrmCollectionShadowReconciliationQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.MunicipalityId is not { } municipalityId
            || municipalityId == Guid.Empty)
            return Result<TrmCollectionShadowReconciliationDto>.Forbidden();

        // TrmTrip.RecordedAt is persisted as a UTC instant. Filter by the inclusive Philippine calendar
        // window, then derive the business date from that same stored instant after materialization.
        var (startUtc, _) = PhilippineTime.DayUtcRange(request.From);
        var (_, endUtc) = PhilippineTime.DayUtcRange(request.To);

        var trips = await context.TrmTrips
            .AsNoTracking()
            .Where(x => x.MunicipalityId == municipalityId
                && x.RecordedAt >= startUtc
                && x.RecordedAt < endUtc)
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.RecordedAt,
                x.Fee,
                x.CollectorId
            })
            .ToListAsync(cancellationToken);

        var sourceRows = trips.Select(x => new
        {
            x.Id,
            BusinessDate = DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(x.RecordedAt)),
            Amount = x.Fee,
            x.CollectorId
        }).ToList();
        var sourceTotal = sourceRows.Sum(x => x.Amount);
        var projectedRows = new List<TrmCollectionShadowProjectedRowDto>();
        var unresolvedRows = new List<TrmCollectionShadowUnresolvedRowDto>();

        if (sourceRows.Count == 0)
        {
            return Result<TrmCollectionShadowReconciliationDto>.Success(
                new(request.From, request.To, 0, 0m, projectedRows, unresolvedRows));
        }

        var classification = await context.RevenueClassifications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.MunicipalityId == municipalityId
                    && x.SemanticCode == RevenueClassificationCodes.TransportationParking,
                cancellationToken);

        if (classification is null)
        {
            unresolvedRows.AddRange(sourceRows.Select(x => new TrmCollectionShadowUnresolvedRowDto(
                x.Id,
                x.BusinessDate,
                x.Amount,
                TrmCollectionShadowUnresolvedReasons.TransportationParkingClassificationMissingCode,
                TrmCollectionShadowUnresolvedReasons.TransportationParkingClassificationMissingMessage)));
        }
        else
        {
            // Retirement changes current setup state, not the meaning of already-received historical money.
            // Do not filter IsActive here. A future-only policy is never eligible for an earlier trip date.
            var policies = await context.RevenueClassificationPolicies
                .AsNoTracking()
                .Where(x => x.MunicipalityId == municipalityId
                    && x.RevenueClassificationId == classification.Id
                    && x.EffectiveDate <= request.To)
                .OrderByDescending(x => x.EffectiveDate)
                .ThenByDescending(x => x.Id)
                .Select(x => new { x.Id, x.EffectiveDate })
                .ToListAsync(cancellationToken);

            foreach (var trip in sourceRows)
            {
                var policy = policies.FirstOrDefault(x => x.EffectiveDate <= trip.BusinessDate);
                if (policy is null)
                {
                    unresolvedRows.Add(new TrmCollectionShadowUnresolvedRowDto(
                        trip.Id,
                        trip.BusinessDate,
                        trip.Amount,
                        TrmCollectionShadowUnresolvedReasons.TransportationParkingPolicyNotEffectiveCode,
                        TrmCollectionShadowUnresolvedReasons.TransportationParkingPolicyNotEffectiveMessage));
                    continue;
                }

                projectedRows.Add(new TrmCollectionShadowProjectedRowDto(
                    trip.Id,
                    trip.BusinessDate,
                    trip.Amount,
                    trip.CollectorId,
                    classification.Id,
                    policy.Id,
                    policy.EffectiveDate,
                    CollectionSourceKind.TrmTrip,
                    trip.Id));
            }
        }

        return Result<TrmCollectionShadowReconciliationDto>.Success(
            new(request.From, request.To, sourceRows.Count, sourceTotal, projectedRows, unresolvedRows));
    }
}
