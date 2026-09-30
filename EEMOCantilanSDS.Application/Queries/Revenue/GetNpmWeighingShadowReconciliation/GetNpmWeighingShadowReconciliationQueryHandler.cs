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
/// activated. Only server-frozen weighing money is projected: Meat, and Fish rows collected after Fish rate evidence
/// began to be frozen (IA-049). Historical Fish rows carry kilos only, so they are
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
                x.MeatKilos, x.MeatFeeRatePerKilo, x.MeatFeeRateEffectiveDate, x.MeatFeeAmount,
                x.FishFeeRatePerKilo, x.FishFeeRateEffectiveDate, x.FishFeeAmountFrozen
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

        // One frozen weighing part (Fish or Meat) becomes a projected row only with complete frozen evidence, a
        // configured classification and a policy effective on the collection date. Otherwise it stays unresolved with
        // its reason; nothing is ever priced from a constant or from today's rate.
        void Resolve(Guid rowId, Guid stallId, DateOnly date, Guid? collectorId, CollectionSourcePart part,
            decimal kilos, decimal? rate, DateOnly? rateDate, decimal? amount)
        {
            var frozen = rate is > 0m && rateDate is not null && amount is > 0m;
            if (!frozen)
            {
                var isFish = part == CollectionSourcePart.FishFee;
                unresolved.Add(new(rowId, date, part, kilos, amount is > 0m ? amount : null,
                    isFish ? NpmWeighingShadowUnresolvedReasons.FishRateNotFrozenCode
                           : NpmWeighingShadowUnresolvedReasons.MeatRateEvidenceMissingCode,
                    isFish ? NpmWeighingShadowUnresolvedReasons.FishRateNotFrozenMessage
                           : NpmWeighingShadowUnresolvedReasons.MeatRateEvidenceMissingMessage));
                if (isFish) fishKilos += kilos;
                return;
            }

            frozenCount++;
            frozenTotal += amount!.Value;
            if (classification is null)
            {
                unresolved.Add(new(rowId, date, part, kilos, amount,
                    NpmWeighingShadowUnresolvedReasons.ClassificationMissingCode,
                    NpmWeighingShadowUnresolvedReasons.ClassificationMissingMessage));
                return;
            }
            var policy = policies.FirstOrDefault(x => x.EffectiveDate <= date);
            if (policy is null)
            {
                unresolved.Add(new(rowId, date, part, kilos, amount,
                    NpmWeighingShadowUnresolvedReasons.PolicyNotEffectiveCode,
                    NpmWeighingShadowUnresolvedReasons.PolicyNotEffectiveMessage));
                return;
            }

            projected.Add(new(rowId, stallId, date, part, kilos, rate!.Value, rateDate!.Value, amount.Value,
                collectorId, classification.Id, policy.Id, policy.EffectiveDate, policy.PermittedInstrumentType,
                CollectionSourceKind.DailyCollection, rowId));
        }

        foreach (var row in rows)
        {
            if (row.FishKilos is > 0m and var fish)
                Resolve(row.Id, row.StallId, row.CollectionDate, row.CollectorId, CollectionSourcePart.FishFee,
                    fish, row.FishFeeRatePerKilo, row.FishFeeRateEffectiveDate, row.FishFeeAmountFrozen);
            if (row.MeatKilos is > 0m and var meat)
                Resolve(row.Id, row.StallId, row.CollectionDate, row.CollectorId, CollectionSourcePart.MeatWeighing,
                    meat, row.MeatFeeRatePerKilo, row.MeatFeeRateEffectiveDate,
                    row.MeatFeeAmount > 0m ? row.MeatFeeAmount : null);
        }

        return Result<NpmWeighingShadowReconciliationDto>.Success(new(
            request.From, request.To, frozenCount, frozenTotal, fishKilos, projected, unresolved));
    }
}
