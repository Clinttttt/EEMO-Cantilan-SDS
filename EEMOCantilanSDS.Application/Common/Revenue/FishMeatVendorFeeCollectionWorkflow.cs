using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>IA-064 additional vendor fee. No rent, monthly assessment or weighing mutation.</summary>
public sealed class FishMeatVendorFeeCollectionWorkflow(IAppDbContext db, ICurrentUserService user,
    ICurrentMunicipalityAccessor municipality, IClock clock)
{
    private const string Origin = "MobileDirectVendorFee";
    private Guid Tenant => municipality.MunicipalityId;
    private async Task<bool> Authorized(CancellationToken ct) => user.IsAuthenticated && user.Role == "Collector"
        && user.CollectorId is { } collector && Tenant != Guid.Empty
        && (!user.MunicipalityId.HasValue || user.MunicipalityId == Tenant)
        && await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.Id == collector && x.IsActive
            && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM), ct);

    public async Task<Result<IReadOnlyList<DirectVendorFeeSource>>> DiscoverAsync(Guid? payer = null, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<IReadOnlyList<DirectVendorFeeSource>>.Forbidden();
        var today = clock.PhilippineToday;
        if (today >= OfficeCollectionWorkflow.EffectiveFrom)
            return Result<IReadOnlyList<DirectVendorFeeSource>>.Success([]); // Compatibility endpoint; current sources are independent registrations.
        var policy = await PolicyAsync(today, ct);
        var stalls = await db.Stalls.AsNoTracking().Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .Where(x => x.MunicipalityId == Tenant && x.Facility!.Code == FacilityCode.NPM
                && (x.Section == MarketSection.FishSection || x.Section == MarketSection.MeatSection)
                && (!payer.HasValue || x.Contracts.Any(c => c.PayorId == payer))).ToListAsync(ct);
        var rows = new List<DirectVendorFeeSource>();
        foreach (var stall in stalls.OrderBy(x => x.StallNo))
        {
            var occupancy = stall.OccupancyAnsweringForMonth(today.Year, today.Month, today)?.Contract;
            if (occupancy is null) continue;
            var payor = occupancy.Payor?.MunicipalityId == Tenant ? occupancy.Payor : null;
            if (payer.HasValue && payor?.Id != payer) continue;
            var eligible = payor is not null && policy is not null && FishMeatVendorFeeRules.UsesDirectCollection(today);
            rows.Add(new(stall.Id, stall.StallNo, stall.Section == MarketSection.FishSection ? "Fish section" : "Meat section",
                payor?.Id, payor?.DisplayName ?? occupancy.ActualOccupant ?? "Occupant", eligible,
                payor is null ? occupancy.PayorId.HasValue ? "The linked Business Payor needs office review." : "Link the Business Payor in NPM."
                    : !eligible ? "Collection is not currently available." : null, occupancy.Id, occupancy.PayorId.HasValue));
        }
        return Result<IReadOnlyList<DirectVendorFeeSource>>.Success(rows);
    }
    private sealed record Facts(DirectVendorFeeQuote Quote, RevenueClassification Classification, RevenueClassificationPolicy Policy);
    private async Task<Facts?> ResolveAsync(DirectVendorFeeRequest request, CancellationToken ct)
    {
        if (request.BusinessDate >= OfficeCollectionWorkflow.EffectiveFrom) return null;
        if (!FishMeatVendorFeeRules.UsesDirectCollection(request.BusinessDate) || request.BusinessDate > clock.PhilippineToday
            || request.AmountReceived <= 0m || request.AmountReceived > Collection.MaximumMoneyAmount
            || decimal.Round(request.AmountReceived, 2) != request.AmountReceived || request.PayorId == Guid.Empty) return null;
        var stall = await db.Stalls.AsNoTracking().Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == request.StallId && x.Facility!.Code == FacilityCode.NPM
                && (x.Section == MarketSection.FishSection || x.Section == MarketSection.MeatSection), ct);
        var contract = stall?.OccupancyAnsweringForMonth(request.BusinessDate.Year, request.BusinessDate.Month, request.BusinessDate)?.Contract;
        if (contract?.PayorId != request.PayorId || contract.Payor?.MunicipalityId != Tenant) return null;
        var policy = await PolicyAsync(request.BusinessDate, ct);
        if (policy is null) return null;
        var source = new DirectVendorFeeSource(stall!.Id, stall.StallNo, stall.Section == MarketSection.FishSection ? "Fish section" : "Meat section",
            request.PayorId, contract.Payor.DisplayName, true, null, contract.Id, true);
        var version = JsonSerializer.Serialize(new { contract.Id, contract.UpdatedAt, request.PayorId, PolicyId = policy.Value.Policy.Id,
            Cutover = FishMeatVendorFeeRules.DirectEffectiveDate });
        return new(new(source, request.AmountReceived, RevenueInstrumentType.OfficialReceipt, version), policy.Value.Classification, policy.Value.Policy);
    }
    private async Task<(RevenueClassification Classification, RevenueClassificationPolicy Policy)?> PolicyAsync(DateOnly date, CancellationToken ct)
    {
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant
            && x.SemanticCode == RevenueClassificationCodes.FishMeatVendorFee && x.IsActive, ct);
        if (classification is null) return null;
        var policy = await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant
            && x.RevenueClassificationId == classification.Id && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= date)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        return policy?.PermittedInstrumentType == RevenueInstrumentType.OfficialReceipt ? (classification, policy) : null;
    }
    public async Task<Result<DirectVendorFeeQuote>> QuoteAsync(DirectVendorFeeRequest request, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<DirectVendorFeeQuote>.Forbidden();
        var facts = await ResolveAsync(request, ct);
        return facts is null ? Result<DirectVendorFeeQuote>.Failure("Choose an eligible linked vendor and a positive amount received.") : Result<DirectVendorFeeQuote>.Success(facts.Quote);
    }
    public async Task<Result<GovernedServiceOutcomeDto>> PostAsync(DirectVendorFeeRequest request, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<GovernedServiceOutcomeDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty) return Result<GovernedServiceOutcomeDto>.Failure("A collection identity is required.");
        var actor = user.CollectorId!.Value.ToString("N");
        var normalized = JsonSerializer.Serialize(new { Version = 1, request.BusinessDate, request.StallId, request.PayorId, request.AmountReceived });
        var fingerprint = PostingOperation.ComputeIntentFingerprint(1, normalized, Origin, actor);
        var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.Origin != Origin || prior.ActorId != actor || prior.IntentFingerprint != fingerprint || prior.CollectionId is null)
                return Result<GovernedServiceOutcomeDto>.Failure("The collection identity belongs to a different intent.", ResultStatus.Conflict);
            var previous = await db.Collections.AsNoTracking().SingleAsync(x => x.MunicipalityId == Tenant && x.Id == prior.CollectionId, ct);
            return Result<GovernedServiceOutcomeDto>.Success(new(previous.Id, previous.ReferenceCode, previous.BusinessDate, previous.TotalAmount, RevenueInstrumentType.OfficialReceipt, "Posted", true));
        }
        var facts = await ResolveAsync(request, ct);
        if (facts is null) return Result<GovernedServiceOutcomeDto>.Failure("Choose an eligible linked vendor and a positive amount received.");
        var snapshot = JsonSerializer.Serialize(new { request.BusinessDate, facts.Quote.Source, facts.Quote.Version, Amount = request.AmountReceived,
            Instrument = RevenueInstrumentType.OfficialReceipt, Classification = RevenueClassificationCodes.FishMeatVendorFee });
        var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(Tenant, request.ClientOperationId, 1, normalized,
            Origin, actor, user.Username ?? "Collector", "Collector", request.BusinessDate, user.Username ?? "Collector",
            [new(facts.Classification, facts.Policy, request.AmountReceived, CollectionSourceKind.FishMeatVendorFee, request.StallId, null, snapshot)],
            collectorId: user.CollectorId, payorId: request.PayorId, payerName: facts.Quote.Source.PayerName, ct: ct);
        return Result<GovernedServiceOutcomeDto>.Success(new(collection.Id, collection.ReferenceCode, collection.BusinessDate, collection.TotalAmount, RevenueInstrumentType.OfficialReceipt, "Posted", false));
    }
}
