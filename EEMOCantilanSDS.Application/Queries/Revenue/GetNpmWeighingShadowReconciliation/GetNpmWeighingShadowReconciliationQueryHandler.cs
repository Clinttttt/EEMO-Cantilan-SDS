using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetNpmWeighingShadowReconciliation;

/// <summary>
/// Shadow reconciliation of NPM weighing facts to WEIGHT_AND_MEASURE, in the same bounded shape as the TPM and TRM
/// shadows: the NPM DailyCollection stays the authority, no Collection/CollectionLine is written, and nothing is
/// activated. Only server-frozen Meat weighing money is projected; Fish weighing has no frozen rate/amount and is
/// reported unresolved rather than priced from a constant or a current rate.
/// </summary>
public sealed class GetNpmWeighingShadowReconciliationQueryHandler(
    IAppDbContext context,
    ICurrentUserService currentUser)
    : IRequestHandler<GetNpmWeighingShadowReconciliationQuery, Result<NpmWeighingShadowReconciliationDto>>
{
    public async Task<Result<NpmWeighingShadowReconciliationDto>> Handle(
        GetNpmWeighingShadowReconciliationQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.MunicipalityId is not { } municipalityId
            || municipalityId == Guid.Empty)
            return Result<NpmWeighingShadowReconciliationDto>.Forbidden();

        var rows = await context.DailyCollections
            .AsNoTracking()
            .Where(x => x.MunicipalityId == municipalityId
                && x.IsPaid
                && x.CollectionDate >= request.From
                && x.CollectionDate <= request.To
                && (x.FishKilos > 0m || x.MeatKilos > 0m))
            .OrderBy(x => x.CollectionDate).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.StallId, x.CollectionDate, x.CollectorId, x.FishKilos,
                x.MeatKilos, x.MeatFeeRatePerKilo, x.MeatFeeRateEffectiveDate, x.MeatFeeAmount
            })
            .ToListAsync(cancellationToken);

        var classification = await context.RevenueClassifications
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.MunicipalityId == municipalityId
                && x.SemanticCode == RevenueClassificationCodes.WeightAndMeasure, cancellationToken);
        // Historical reconciliation ignores current IsActive: retirement must not erase an earlier date's identity.
        var policies = classification is null
            ? []
            : await context.RevenueClassificationPolicies
                .AsNoTracking()
                .Where(x => x.MunicipalityId == municipalityId
                    && x.RevenueClassificationId == classification.Id
                    && x.BusinessContext == RevenuePolicyContext.Default
                    && x.EffectiveDate <= request.To)
                .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id)
                .Select(x => new { x.Id, x.EffectiveDate, x.PermittedInstrumentType })
                .ToListAsync(cancellationToken);

        var projected = new List<NpmWeighingShadowProjectedRowDto>();
        var unresolved = new List<NpmWeighingShadowUnresolvedRowDto>();
        var frozenCount = 0;
        var frozenTotal = 0m;
        var fishKilos = 0m;

        foreach (var row in rows)
        {
            if (row.FishKilos is > 0m and var fish)
            {
                fishKilos += fish;
                unresolved.Add(new(row.Id, row.CollectionDate, CollectionSourcePart.FishFee, fish, null,
                    NpmWeighingShadowUnresolvedReasons.FishRateNotFrozenCode,
                    NpmWeighingShadowUnresolvedReasons.FishRateNotFrozenMessage));
            }

            if (row.MeatKilos is not (> 0m and var meat))
                continue;
            if (row.MeatFeeRatePerKilo is not (> 0m and var rate)
                || row.MeatFeeRateEffectiveDate is not { } rateDate
                || row.MeatFeeAmount <= 0m)
            {
                unresolved.Add(new(row.Id, row.CollectionDate, CollectionSourcePart.MeatWeighing, meat,
                    row.MeatFeeAmount > 0m ? row.MeatFeeAmount : null,
                    NpmWeighingShadowUnresolvedReasons.MeatRateEvidenceMissingCode,
                    NpmWeighingShadowUnresolvedReasons.MeatRateEvidenceMissingMessage));
                continue;
            }

            frozenCount++;
            frozenTotal += row.MeatFeeAmount;
            if (classification is null)
            {
                unresolved.Add(new(row.Id, row.CollectionDate, CollectionSourcePart.MeatWeighing, meat, row.MeatFeeAmount,
                    NpmWeighingShadowUnresolvedReasons.ClassificationMissingCode,
                    NpmWeighingShadowUnresolvedReasons.ClassificationMissingMessage));
                continue;
            }
            var policy = policies.FirstOrDefault(x => x.EffectiveDate <= row.CollectionDate);
            if (policy is null)
            {
                unresolved.Add(new(row.Id, row.CollectionDate, CollectionSourcePart.MeatWeighing, meat, row.MeatFeeAmount,
                    NpmWeighingShadowUnresolvedReasons.PolicyNotEffectiveCode,
                    NpmWeighingShadowUnresolvedReasons.PolicyNotEffectiveMessage));
                continue;
            }

            projected.Add(new(row.Id, row.StallId, row.CollectionDate, CollectionSourcePart.MeatWeighing, meat, rate,
                rateDate, row.MeatFeeAmount, row.CollectorId, classification.Id, policy.Id, policy.EffectiveDate,
                policy.PermittedInstrumentType, CollectionSourceKind.DailyCollection, row.Id));
        }

        return Result<NpmWeighingShadowReconciliationDto>.Success(new(
            request.From, request.To, frozenCount, frozenTotal, fishKilos, projected, unresolved));
    }
}
