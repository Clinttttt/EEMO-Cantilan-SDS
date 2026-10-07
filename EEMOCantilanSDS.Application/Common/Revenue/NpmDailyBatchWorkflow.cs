using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Today's checked stalls only. The existing daily handler owns every charge; one atomic checkout, independent SRCs.</summary>
public sealed class NpmDailyBatchWorkflow(IAppDbContext db, ICollectionSessionStore store, NpmDailyCanonicalPoster poster,
    INpmMonthSettlementService months, IFeeRateResolver rates, ISender sender, ICurrentUserService user,
    ICurrentMunicipalityAccessor tenant, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid Tenant => tenant.MunicipalityId;
    private async Task<bool> Authorized(CancellationToken ct) => user.IsAuthenticated && user.Role == "Collector"
        && Tenant != Guid.Empty && (!user.MunicipalityId.HasValue || user.MunicipalityId == Tenant)
        && user.CollectorId is { } id && await store.IsActiveCollectorAsync(Tenant, id, ct)
        && await db.CollectorUsers.AnyAsync(x => x.Id == id && x.MunicipalityId == Tenant && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM), ct);
    public async Task<Result<IReadOnlyList<NpmDailyBatchSource>>> SourcesAsync(CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<IReadOnlyList<NpmDailyBatchSource>>.Forbidden();
        var today = clock.PhilippineToday;
        var ids = await db.Stalls.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Facility!.Code == FacilityCode.NPM)
            .OrderBy(x => x.StallNo).Select(x => x.Id).ToListAsync(ct);
        var sources = new List<NpmDailyBatchSource>();
        foreach (var chunk in ids.Chunk(500))
            sources.AddRange((await PreviewCore(new(Guid.NewGuid(), today, chunk.Select(x => new NpmDailyBatchItem(x, x)).ToArray()), ct)).Sources);
        return Result<IReadOnlyList<NpmDailyBatchSource>>.Success(sources);
    }
    public async Task<Result<NpmDailyBatchReadiness>> ReadinessAsync(CancellationToken ct = default)
    {
        var sources = await SourcesAsync(ct);
        return sources.IsSuccess ? Result<NpmDailyBatchReadiness>.Success(new(clock.PhilippineToday, sources.Value!))
            : Result<NpmDailyBatchReadiness>.Failure(sources.Error ?? "CollectorNotAssigned", sources.Status);
    }
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    private string IntentHash(NpmDailyBatchIntent intent) => Hash(new { Kind = "NpmDailyBatchV1", Tenant, user.CollectorId,
        intent.BusinessDate, Items = intent.Items.OrderBy(x => x.ClientItemId).ToArray() });
    public async Task<Result<NpmDailyBatchQuote>> PreviewAsync(NpmDailyBatchIntent intent, CancellationToken ct = default) =>
        !await Authorized(ct) ? Result<NpmDailyBatchQuote>.Forbidden() : Result<NpmDailyBatchQuote>.Success(await PreviewCore(intent, ct));
    private async Task<NpmDailyBatchQuote> PreviewCore(NpmDailyBatchIntent intent, CancellationToken ct)
    {
        var problems = new List<CollectionSessionProblem>(); var sources = new List<NpmDailyBatchSource>();
        if (intent.BusinessDate != clock.PhilippineToday || intent.ClientCollectionSessionId == Guid.Empty || intent.Items is null
            || intent.Items.Count is < 1 or > 500 || intent.Items.Any(x => x.ClientItemId == Guid.Empty || x.StallId == Guid.Empty)
            || intent.Items.Select(x => x.StallId).Distinct().Count() != intent.Items.Count || intent.Items.Select(x => x.ClientItemId).Distinct().Count() != intent.Items.Count)
            return new(intent, [], 0m, null, [new(null, "InvalidIntent", "Select today's eligible stalls.")]);
        var canonical = await poster.IsCanonicalAsync(intent.BusinessDate, ct);
        var instrument = await poster.GetInstrumentAsync(intent.BusinessDate, ct);
        var snapshot = await rates.GetSnapshotAsync(ct);
        var ids = intent.Items.Select(x => x.StallId).ToArray();
        var stalls = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts)
            .Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id) && x.Facility!.Code == FacilityCode.NPM).ToDictionaryAsync(x => x.Id, ct);
        var policy = await (from p in db.RevenueClassificationPolicies.AsNoTracking()
            join c in db.RevenueClassifications.AsNoTracking() on p.RevenueClassificationId equals c.Id
            where c.MunicipalityId == Tenant && c.SemanticCode == RevenueClassificationCodes.PermanentStallRent && p.EffectiveDate <= intent.BusinessDate
                && p.BusinessContext == RevenuePolicyContext.Default
            orderby p.EffectiveDate descending select p.Id).FirstOrDefaultAsync(ct);
        foreach (var item in intent.Items.OrderBy(x => x.ClientItemId))
        {
            if (!stalls.TryGetValue(item.StallId, out var stall)) { problems.Add(new(item.ClientItemId, "InvalidSource", "This stall is unavailable.")); continue; }
            var owner = stall.OccupancyAnsweringForMonth(intent.BusinessDate.Year, intent.BusinessDate.Month, intent.BusinessDate)?.Contract;
            var row = await db.DailyCollections.AsNoTracking().SingleOrDefaultAsync(x => x.StallId == stall.Id && x.CollectionDate == intent.BusinessDate, ct);
            var charge = row?.DailyFee ?? NpmDailyFee.ForStallOrNull(stall, snapshot, intent.BusinessDate) ?? 0m;
            var payable = owner is not null && (await months.GetPayableDaysAsync(stall, intent.BusinessDate.Year, intent.BusinessDate.Month, ct)).Contains(intent.BusinessDate);
            var reason = !canonical ? "SourceStillLegacy" : instrument is null ? "PolicyNotEffective" : owner is null ? "InvalidSource"
                : row?.IsPaid == true ? "AlreadyCollected" : !payable ? "AlreadySettledOrUnavailable" : charge <= 0m ? "RateNotEffective" : null;
            sources.Add(new(stall.Id, owner?.Id ?? Guid.Empty, stall.StallNo, owner?.ActualOccupant ?? "", charge, reason is null, reason));
            if (reason is not null) problems.Add(new(item.ClientItemId, reason, "Review this stall before collecting."));
        }
        var rateEvidence = await db.FacilityRates.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.FacilityCode == FacilityCode.NPM && x.EffectiveDate <= intent.BusinessDate)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.RateKey, x.Amount, x.EffectiveDate, x.UpdatedAt }).ToListAsync(ct);
        var sectionEvidence = await db.FacilitySectionRates.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.FacilityCode == FacilityCode.NPM && x.EffectiveDate <= intent.BusinessDate)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Amount, x.EffectiveDate, x.UpdatedAt }).ToListAsync(ct);
        var token = problems.Count == 0 ? Hash(new { Intent = IntentHash(intent), policy, instrument, Sources = sources, rateEvidence, sectionEvidence }) : null;
        return new(intent, sources, sources.Where(x => x.CanCollect).Sum(x => x.EffectiveCharge), token, problems);
    }
    public async Task<Result<CollectionSessionResult>> RecordAsync(RecordNpmDailyBatchRequest request, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<CollectionSessionResult>.Forbidden();
        var intent = request.Intent;
        if (intent.Items is null) return Result<CollectionSessionResult>.Failure("InvalidIntent", ResultStatus.Invalid);
        var fingerprint = IntentHash(intent);
        async Task<CollectionSessionResult?> Replay()
        {
            var prior = await store.FindAsync(Tenant, intent.ClientCollectionSessionId, ct);
            if (prior is null) return null;
            if (prior.CollectorId != user.CollectorId || prior.IntentFingerprint != fingerprint) throw new BatchRejected("SessionIntentConflict");
            var result = JsonSerializer.Deserialize<CollectionSessionResult>(prior.ResultJson, Json)!;
            return result with { ExistingOutcome = true, Collections = await store.RefreshDispositionAsync(result.Collections, ct) };
        }
        try
        {
            if (await Replay() is { } replay) return Result<CollectionSessionResult>.Success(replay);
            CollectionSessionResult? output = null;
            await store.ExecuteAtomicallyAsync(async () =>
            {
                if (await Replay() is { } concurrent) { output = concurrent; return; }
                var quote = await PreviewCore(intent, ct);
                if (!quote.CanRecord || request.QuoteFingerprint != quote.QuoteFingerprint) throw new BatchRejected("QuoteStale");
                var children = new List<CollectionSessionCollection>();
                foreach (var item in intent.Items.OrderBy(x => x.ClientItemId))
                {
                    var operation = CollectionSessionWorkflow.ChildOperationId(Tenant, intent.ClientCollectionSessionId, item.ClientItemId);
                    var posted = await sender.Send(new RecordDailyCollectionCommand(item.StallId, intent.BusinessDate, true, ClientOperationId: operation), ct);
                    var child = posted.IsSuccess ? await poster.FindPostedAsync(operation, ct) : null;
                    if (child is null || child.Amount != quote.Sources.Single(x => x.StallId == item.StallId).EffectiveCharge) throw new BatchRejected("QuoteStale");
                    children.Add(new(child.CollectionId, child.ReferenceCode, (await poster.GetInstrumentAsync(intent.BusinessDate, ct))!.Value, child.Amount, [item.ClientItemId], "Posted"));
                }
                output = new(intent.ClientCollectionSessionId, CollectionSessionStatus.Recorded, null,
                    children.Sum(x => x.Amount), children, []);
                await store.SaveAsync(MobileCollectionSession.Recorded(Tenant, intent.ClientCollectionSessionId, user.CollectorId!.Value,
                    null, intent.BusinessDate, fingerprint, JsonSerializer.Serialize(output, Json)), ct);
            }, ct);
            return Result<CollectionSessionResult>.Success(output!);
        }
        catch (BatchRejected e) { return Result<CollectionSessionResult>.Failure(e.Message, ResultStatus.Conflict); }
        catch (CollectionSessionConcurrencyException)
        {
            try { if (await Replay() is { } replay) return Result<CollectionSessionResult>.Success(replay); }
            catch (BatchRejected e) { return Result<CollectionSessionResult>.Failure(e.Message, ResultStatus.Conflict); }
            return Result<CollectionSessionResult>.Failure("RetrySession", ResultStatus.Conflict);
        }
    }
    private sealed class BatchRejected(string code) : Exception(code);
}
