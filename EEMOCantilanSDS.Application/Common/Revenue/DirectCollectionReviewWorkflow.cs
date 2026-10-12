using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The Head's central view of Collections a Collector recorded directly, with no office-prepared amount. It READS the canonical Collections
/// (so it can never diverge from the SRC ledger) and adds only an append-only administrative review event: Confirm, Needs correction or
/// Resolve. A review never creates a Collection, an SRC, an allocation or any amount, never reverses money, and never removes cash from
/// collection and remittance accountability - a posted Collection counts as received whether or not anyone has reviewed it.
/// </summary>
public sealed class DirectCollectionReviewWorkflow(IAppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor municipality, IClock clock)
{
    public const int MaxPage = 100;
    public const int MaxBulk = 100;

    // Operations where money is recorded with no office-prepared amount: the source is the registered vendor, the service, the weighing,
    // the terminal section or the fine authority. A prepared obligation (NPM daily/rent, assessed periods, fine assessments) is not direct.
    private static readonly CollectionSourceKind[] DirectKinds =
    [
        CollectionSourceKind.GovernedService, CollectionSourceKind.NpmWeighing, CollectionSourceKind.FishMeatVendorFee,
        CollectionSourceKind.FishMeatVendorRegistration, CollectionSourceKind.TerminalSection, CollectionSourceKind.DirectFineCollection
    ];

    private bool TryTenant(out Guid tenant)
    {
        tenant = municipality.MunicipalityId;
        return user.IsAuthenticated && user.Role is "SuperAdmin" or "Admin" && user.UserId is { } id && id != Guid.Empty
            && tenant != Guid.Empty && (!user.MunicipalityId.HasValue || user.MunicipalityId == tenant);
    }

    private IQueryable<Collection> DirectCollections(Guid tenant)
    {
        var directLines = db.CollectionLines.AsNoTracking()
            .Where(l => l.MunicipalityId == tenant && l.SourceKind != null && DirectKinds.Contains(l.SourceKind.Value))
            .Select(l => l.CollectionId);
        var directUtility = from a in db.CollectionAllocations.AsNoTracking()
                            join l in db.CollectionLines.AsNoTracking() on a.CollectionLineId equals l.Id
                            join b in db.UtilityBills.AsNoTracking() on a.SourceId equals b.Id
                            where a.MunicipalityId == tenant && a.SourceKind == CollectionSourceKind.UtilityBill
                                && ((a.SourcePart == CollectionSourcePart.Electricity && b.ElectricityDirectCollection)
                                    || (a.SourcePart == CollectionSourcePart.Water && b.WaterDirectCollection))
                            select l.CollectionId;
        return db.Collections.AsNoTracking().Where(c => c.MunicipalityId == tenant && c.CollectorId != null
            && (directLines.Contains(c.Id) || directUtility.Contains(c.Id)));
    }

    private IQueryable<Guid> ReversedIds(Guid tenant) => db.CollectionCorrections.AsNoTracking()
        .Where(x => x.MunicipalityId == tenant && x.FinancialEffectAmount < 0m).Select(x => x.OriginalCollectionId);

    private IQueryable<DirectCollectionReviewEvent> Events(Guid tenant) => db.DirectCollectionReviewEvents.AsNoTracking().Where(e => e.MunicipalityId == tenant);

    public async Task<Result<DirectCollectionReviewPageDto>> GetAsync(DirectCollectionReviewRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenant)) return Result<DirectCollectionReviewPageDto>.Forbidden();
        var today = clock.PhilippineToday;
        var to = request.To ?? today; var from = request.From ?? to.AddDays(-30);
        if (from > to || to.DayNumber - from.DayNumber > 366)
            return Result<DirectCollectionReviewPageDto>.Failure("Choose a period of no more than 367 days.", ResultStatus.Invalid);
        var take = Math.Clamp(request.Take <= 0 ? 25 : request.Take, 1, MaxPage); var skip = Math.Max(0, request.Skip);

        var inRange = DirectCollections(tenant).Where(c => c.BusinessDate >= from && c.BusinessDate <= to);
        var reversed = ReversedIds(tenant); var events = Events(tenant);
        var pendingCount = await inRange.Where(c => !reversed.Contains(c.Id) && !events.Any(e => e.CollectionId == c.Id)).CountAsync(ct);

        var q = inRange;
        q = (request.State ?? "Pending") switch
        {
            "Pending" => q.Where(c => !reversed.Contains(c.Id) && !events.Any(e => e.CollectionId == c.Id)),
            "NeedsCorrection" => q.Where(c => events.Where(e => e.CollectionId == c.Id).OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).Select(e => e.Outcome).FirstOrDefault() == DirectCollectionReviewOutcome.NeedsCorrection),
            "Reviewed" => q.Where(c => events.Any(e => e.CollectionId == c.Id) && events.Where(e => e.CollectionId == c.Id).OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).Select(e => e.Outcome).FirstOrDefault() != DirectCollectionReviewOutcome.NeedsCorrection),
            "All" => q,
            _ => q.Where(c => false)
        };
        if (!string.IsNullOrWhiteSpace(request.Collector))
        {
            var collector = request.Collector.Trim().ToLower();
            q = q.Where(c => c.ActorName.ToLower().Contains(collector));
        }
        if (!string.IsNullOrWhiteSpace(request.Operation))
        {
            var operation = request.Operation.Trim().ToLower();
            q = q.Where(c => db.CollectionLines.Any(l => l.CollectionId == c.Id && l.MunicipalityId == tenant
                && db.RevenueClassificationPolicies.Any(p => p.Id == l.RevenueClassificationPolicyId && p.DisplayName.ToLower().Contains(operation))));
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            q = q.Where(c => c.ReferenceCode.ToLower().Contains(term) || (c.PayerName != null && c.PayerName.ToLower().Contains(term)) || c.ActorName.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(c => c.RecordedAtUtc).ThenByDescending(c => c.Id).Skip(skip).Take(take).ToListAsync(ct);
        return Result<DirectCollectionReviewPageDto>.Success(new(await ProjectAsync(tenant, page, ct), total, pendingCount));
    }

    private async Task<IReadOnlyList<DirectCollectionReviewRowDto>> ProjectAsync(Guid tenant, List<Collection> page, CancellationToken ct)
    {
        var ids = page.Select(c => c.Id).ToArray();
        var lines = await db.CollectionLines.AsNoTracking().Where(l => l.MunicipalityId == tenant && ids.Contains(l.CollectionId)).ToListAsync(ct);
        var policyIds = lines.Select(l => l.RevenueClassificationPolicyId).Distinct().ToArray();
        var policies = await db.RevenueClassificationPolicies.AsNoTracking().Where(p => p.MunicipalityId == tenant && policyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        var lineIds = lines.Select(l => l.Id).ToArray();
        var utilityAllocations = await db.CollectionAllocations.AsNoTracking()
            .Where(a => a.MunicipalityId == tenant && a.SourceKind == CollectionSourceKind.UtilityBill && lineIds.Contains(a.CollectionLineId)).ToListAsync(ct);
        var billIds = utilityAllocations.Select(a => a.SourceId).Distinct().ToArray();
        var bills = await db.UtilityBills.AsNoTracking().Where(b => b.MunicipalityId == tenant && billIds.Contains(b.Id))
            .Select(b => new { b.Id, b.StallId, StallNo = b.Stall!.StallNo, Facility = b.Stall.Facility!.Name }).ToDictionaryAsync(b => b.Id, ct);
        var reversedSet = (await db.CollectionCorrections.AsNoTracking().Where(x => x.MunicipalityId == tenant && ids.Contains(x.OriginalCollectionId))
            .Select(x => new { x.OriginalCollectionId, x.FinancialEffectAmount, x.CorrectionType }).ToListAsync(ct));
        var events = await db.DirectCollectionReviewEvents.AsNoTracking().Where(e => e.MunicipalityId == tenant && ids.Contains(e.CollectionId)).ToListAsync(ct);

        // The same utility source, part, business date and amount posted more than once and still valid: worth a second look, never an accusation.
        var sameBillAllocations = billIds.Length == 0 ? [] : await (from a in db.CollectionAllocations.AsNoTracking()
                                  join l in db.CollectionLines.AsNoTracking() on a.CollectionLineId equals l.Id
                                  join c in db.Collections.AsNoTracking() on l.CollectionId equals c.Id
                                  where a.MunicipalityId == tenant && a.SourceKind == CollectionSourceKind.UtilityBill && billIds.Contains(a.SourceId)
                                      && !ReversedIds(tenant).Contains(c.Id)
                                  select new { a.SourceId, a.SourcePart, a.Amount, c.BusinessDate, CollectionId = c.Id }).ToListAsync(ct);

        return page.Select(c =>
        {
            var own = lines.Where(l => l.CollectionId == c.Id).ToList();
            var allocations = utilityAllocations.Where(a => own.Any(l => l.Id == a.CollectionLineId)).ToList();
            var utility = allocations.FirstOrDefault()?.SourcePart switch
            {
                CollectionSourcePart.Electricity => "ECF", CollectionSourcePart.Water => "WCF", _ => null
            };
            var first = allocations.FirstOrDefault();
            var source = first is not null && bills.TryGetValue(first.SourceId, out var bill) ? $"{bill.Facility} · Stall {bill.StallNo}" : "";
            var operation = string.Join(" · ", own.Select(l => policies.GetValueOrDefault(l.RevenueClassificationPolicyId, "Collection")).Distinct());
            var corrections = reversedSet.Where(x => x.OriginalCollectionId == c.Id).ToList();
            var disposition = corrections.Any(x => x.FinancialEffectAmount < 0m) ? "Reversed"
                : corrections.Any(x => x.CorrectionType == CollectionCorrectionType.DocumentCorrection) ? "Document corrected" : "Posted";
            var last = events.Where(e => e.CollectionId == c.Id).OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).FirstOrDefault();
            var duplicate = allocations.Any(a => sameBillAllocations.Count(x => x.SourceId == a.SourceId && x.SourcePart == a.SourcePart
                && x.BusinessDate == c.BusinessDate && x.Amount == a.Amount) > 1) && disposition != "Reversed";
            var state = last is null ? "Awaiting review" : last.Outcome switch
            {
                DirectCollectionReviewOutcome.Confirmed => "Confirmed",
                DirectCollectionReviewOutcome.NeedsCorrection => "Needs correction",
                _ => "Resolved"
            };
            return new DirectCollectionReviewRowDto(c.Id, c.ReferenceCode, c.BusinessDate, c.RecordedAtUtc, operation, utility,
                string.IsNullOrWhiteSpace(c.PayerName) ? "No payer named" : c.PayerName, source, c.TotalAmount, c.ActorName, "Direct",
                first is not null ? "Source verified" : "Source-native collection", disposition, state, last?.Note, last?.ActorName, last?.OccurredAtUtc,
                duplicate, last is null && disposition == "Posted" && !duplicate);
        }).ToList();
    }

    public async Task<Result<DirectCollectionReviewRowDto>> ReviewAsync(DirectCollectionReviewActionRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenant)) return Result<DirectCollectionReviewRowDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty || request.CollectionId == Guid.Empty || !Enum.TryParse<DirectCollectionReviewOutcome>(request.Outcome, true, out var outcome) || !Enum.IsDefined(outcome))
            return Result<DirectCollectionReviewRowDto>.Failure("Choose a valid review action.", ResultStatus.Invalid);
        if (outcome == DirectCollectionReviewOutcome.NeedsCorrection && string.IsNullOrWhiteSpace(request.Note))
            return Result<DirectCollectionReviewRowDto>.Failure("Say what needs correcting.", ResultStatus.Invalid);
        if (request.Note?.Trim().Length > 500) return Result<DirectCollectionReviewRowDto>.Failure("Use a note of at most 500 characters.", ResultStatus.Invalid);

        // A retry of the same operation answers with what it already recorded: one event, never a second.
        var prior = await db.DirectCollectionReviewEvents.AsNoTracking().SingleOrDefaultAsync(e => e.MunicipalityId == tenant && e.ClientOperationId == request.ClientOperationId, ct);
        var collection = await DirectCollections(tenant).SingleOrDefaultAsync(c => c.Id == request.CollectionId, ct);
        if (collection is null) return Result<DirectCollectionReviewRowDto>.NotFound();
        if (prior is not null)
            return prior.CollectionId == request.CollectionId && prior.Outcome == outcome
                ? Result<DirectCollectionReviewRowDto>.Success((await ProjectAsync(tenant, [collection], ct))[0])
                : Result<DirectCollectionReviewRowDto>.Failure("This operation id was already used for a different review.", ResultStatus.Conflict);

        var row = (await ProjectAsync(tenant, [collection], ct))[0];
        if (outcome == DirectCollectionReviewOutcome.Confirmed && row.Disposition == "Reversed")
            return Result<DirectCollectionReviewRowDto>.Failure("This collection was reversed; there is nothing to confirm.", ResultStatus.Conflict);
        if (outcome == DirectCollectionReviewOutcome.Resolved && row.ReviewState != "Needs correction")
            return Result<DirectCollectionReviewRowDto>.Failure("Only a collection flagged for correction can be resolved.", ResultStatus.Conflict);

        db.DirectCollectionReviewEvents.Add(DirectCollectionReviewEvent.Record(tenant, collection.Id, request.ClientOperationId, outcome, request.Note,
            user.UserId!.Value, user.Username ?? "Office User"));
        await db.SaveChangesAsync(ct);
        return Result<DirectCollectionReviewRowDto>.Success((await ProjectAsync(tenant, [collection], ct))[0]);
    }

    /// <summary>Confirms only collections that are still awaiting review, posted, and not flagged as a possible duplicate. Everything else is skipped with its reason.</summary>
    public async Task<Result<DirectCollectionBulkConfirmResult>> BulkConfirmAsync(DirectCollectionBulkConfirmRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenant)) return Result<DirectCollectionBulkConfirmResult>.Forbidden();
        var ids = request.CollectionIds?.Where(x => x != Guid.Empty).Distinct().ToArray() ?? [];
        if (request.ClientOperationId == Guid.Empty || ids.Length == 0 || ids.Length > MaxBulk)
            return Result<DirectCollectionBulkConfirmResult>.Failure($"Choose between 1 and {MaxBulk} collections.", ResultStatus.Invalid);
        var collections = await DirectCollections(tenant).Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var rows = (await ProjectAsync(tenant, collections, ct)).ToDictionary(r => r.CollectionId);
        var confirmed = 0; var skipped = new List<DirectCollectionBulkSkip>();
        foreach (var id in ids)
        {
            if (!rows.TryGetValue(id, out var row)) { skipped.Add(new(id, "Not a direct collection of this office.")); continue; }
            if (row.Disposition != "Posted") { skipped.Add(new(id, "Not in its original posted state.")); continue; }
            if (row.ReviewState != "Awaiting review") { skipped.Add(new(id, "Already reviewed.")); continue; }
            if (row.PossibleDuplicate) { skipped.Add(new(id, "Possible duplicate: review it on its own.")); continue; }
            // Each item has its own deterministic operation id, so repeating the same bulk request never adds a second event.
            var operation = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{request.ClientOperationId:N}|{id:N}")));
            if (await db.DirectCollectionReviewEvents.AnyAsync(e => e.MunicipalityId == tenant && e.ClientOperationId == operation, ct)) { confirmed++; continue; }
            db.DirectCollectionReviewEvents.Add(DirectCollectionReviewEvent.Record(tenant, id, operation, DirectCollectionReviewOutcome.Confirmed,
                "Confirmed in bulk review.", user.UserId!.Value, user.Username ?? "Office User"));
            confirmed++;
        }
        await db.SaveChangesAsync(ct);
        return Result<DirectCollectionBulkConfirmResult>.Success(new(confirmed, skipped));
    }
}
