using System.Globalization;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Office Cash Turnover: cash the Head collected directly, accounted for apart from collector remittance. It covers whole canonical
/// Collections that have no collector, derives each one's cash from the Collection net of its linked corrections, and records what was
/// handed over with an evidence reference the Head enters. The Head (SuperAdmin) records and voids; Admin reads; collectors and other
/// tenants are refused. It creates no Collection, no receipt and no deposit, and changes nothing a report counts as income.
/// </summary>
public sealed class OfficeCashTurnoverWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    OfficeCashTurnoverOptions? options = null,
    IClock? clock = null)
{
    private const string Origin = "OfficeCashTurnover";
    private const int IntentVersion = 1;
    private const int MaximumCollections = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;
    private DateTime UtcNow => clock?.UtcNow ?? DateTime.UtcNow;
    private sealed record Actor(Guid TenantId, string Username, string ActorId);

    // ── Reads (Head and Admin) ─────────────────────────────────────────────────────────────────────────

    public Task<Result<OfficeCashWorkspaceDto>> GetWorkspaceAsync(OfficeCashFilterRequest request, CancellationToken ct = default) =>
        Run<OfficeCashWorkspaceDto>(false, async actor =>
        {
            if (Validate(request.From, request.To) is { } problem)
                return Result<OfficeCashWorkspaceDto>.Failure(problem, ResultStatus.Invalid);
            if (request.Instrument is { } instrument && instrument is not (RevenueInstrumentType.CashTicket or RevenueInstrumentType.OfficialReceipt))
                return Result<OfficeCashWorkspaceDto>.Failure("Choose OR and CT, Cash Ticket, or Official Receipt.", ResultStatus.Invalid);

            var office = db.Collections.AsNoTracking().Where(x => x.MunicipalityId == actor.TenantId && x.CollectorId == null);
            var recordedActors = (await office.Where(x => x.ActorRole == "SuperAdmin" || x.ActorRole == "Admin")
                .Select(x => x.ActorId).Distinct().ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var users = await db.AdminUsers.AsNoTracking().Where(x => x.MunicipalityId == actor.TenantId && x.IsActive && !x.IsPlatformOperator
                && (x.Role == AdminRole.SuperAdmin || x.Role == AdminRole.Admin)).Select(x => new { x.Id, x.FullName, x.Username }).ToListAsync(ct);
            var authorities = users.Where(x => recordedActors.Contains(x.Id.ToString("N")) || recordedActors.Contains(x.Id.ToString("D")))
                .Select(x => new OfficeCashAuthorityDto(x.Id, x.FullName ?? x.Username ?? x.Id.ToString("D")))
                .OrderBy(x => x.Name).ThenBy(x => x.Id).ToList();
            if (request.AuthorityId is { } authority && !authorities.Any(x => x.Id == authority))
                return Result<OfficeCashWorkspaceDto>.Failure("Choose an Office authority from this municipality.", ResultStatus.Invalid);

            List<Guid>? collectionIds = null;
            List<Guid>? turnoverIds = null;
            if (request.AuthorityId.HasValue || request.Instrument.HasValue)
            {
                var idsInRegister = db.OfficeCashTurnovers.Where(x => x.MunicipalityId == actor.TenantId
                    && x.TurnoverDate >= request.From && x.TurnoverDate <= request.To).Select(x => x.Id);
                var relevantCoverage = db.OfficeCashTurnoverCoverages.Where(x => x.MunicipalityId == actor.TenantId
                    && idsInRegister.Contains(x.TurnoverId));
                office = office.Where(x => (x.BusinessDate >= request.From && x.BusinessDate <= request.To)
                    || relevantCoverage.Any(c => c.CollectionId == x.Id));
                if (request.AuthorityId is { } selected)
                {
                    var compact = selected.ToString("N");
                    var dashed = selected.ToString("D");
                    office = office.Where(x => x.ActorId == compact || x.ActorId == dashed);
                }
                if (request.Instrument is { } chosen)
                {
                    // Physical evidence takes precedence; otherwise use the recorded policy, never today's configuration.
                    var documents = db.AccountableDocuments.AsNoTracking().Where(x => x.MunicipalityId == actor.TenantId);
                    var policies = db.RevenueClassificationPolicies.AsNoTracking()
                        .Where(x => x.MunicipalityId == actor.TenantId && x.PermittedInstrumentType == chosen).Select(x => x.Id);
                    var lines = db.CollectionLines.AsNoTracking().Where(x => x.MunicipalityId == actor.TenantId);
                    if (await office.AnyAsync(x =>
                        (documents.Any(d => d.CollectionId == x.Id && d.InstrumentType == chosen)
                            && documents.Any(d => d.CollectionId == x.Id && d.InstrumentType != chosen))
                        || (!documents.Any(d => d.CollectionId == x.Id)
                            && lines.Any(l => l.CollectionId == x.Id && policies.Contains(l.RevenueClassificationPolicyId))
                            && lines.Any(l => l.CollectionId == x.Id && !policies.Contains(l.RevenueClassificationPolicyId))), ct))
                        return Result<OfficeCashWorkspaceDto>.Failure(
                            "A collection has mixed or unresolved instruments. Use OR and CT to review its full cash amount.", ResultStatus.Conflict);
                    office = office.Where(x =>
                        (documents.Any(d => d.CollectionId == x.Id && d.InstrumentType == chosen)
                            && !documents.Any(d => d.CollectionId == x.Id && d.InstrumentType != chosen))
                        || (!documents.Any(d => d.CollectionId == x.Id)
                            && lines.Any(l => l.CollectionId == x.Id && policies.Contains(l.RevenueClassificationPolicyId))
                            && !lines.Any(l => l.CollectionId == x.Id && !policies.Contains(l.RevenueClassificationPolicyId))));
                }
                collectionIds = await office.Select(x => x.Id).ToListAsync(ct);
                var coverage = await db.OfficeCashTurnoverCoverages.AsNoTracking()
                    .Where(x => x.MunicipalityId == actor.TenantId && idsInRegister.Contains(x.TurnoverId))
                    .Select(x => new { x.TurnoverId, x.CollectionId }).ToListAsync(ct);
                var matching = collectionIds.ToHashSet();
                var groups = coverage.GroupBy(x => x.TurnoverId).ToList();
                if (groups.Any(g => g.Any(x => matching.Contains(x.CollectionId)) && g.Any(x => !matching.Contains(x.CollectionId))))
                    return Result<OfficeCashWorkspaceDto>.Failure(
                        "This filter splits a turnover covering multiple authorities or instruments. Use All authorities and OR and CT to review its full amount and difference.", ResultStatus.Conflict);
                turnoverIds = groups.Where(g => g.All(x => matching.Contains(x.CollectionId))).Select(g => g.Key).ToList();
            }
            var position = await GetPositionAsync(request.From, request.To, ct, collectionIds, turnoverIds);
            var review = await GetReviewAsync(new(request.From, request.To), ct, collectionIds);
            var register = await GetRegisterAsync(request.From, request.To, ct, turnoverIds);
            if (!position.IsSuccess || !review.IsSuccess || !register.IsSuccess)
                return Result<OfficeCashWorkspaceDto>.Failure(position.Error ?? review.Error ?? register.Error ?? "Office cash could not be loaded.");
            return Result<OfficeCashWorkspaceDto>.Success(new(position.Value!, review.Value!, register.Value!, authorities));
        }, ct);

    public Task<Result<OfficeCashTurnoverReviewDto>> GetReviewAsync(OfficeCashTurnoverReviewRequest request, CancellationToken ct = default,
        IReadOnlyList<Guid>? collectionIds = null) =>
        Run<OfficeCashTurnoverReviewDto>(false, async actor =>
        {
            if (Validate(request.From, request.To) is { } problem) return Result<OfficeCashTurnoverReviewDto>.Failure(problem, ResultStatus.Invalid);
            var eligible = await EligibleAsync(actor.TenantId, request.From, request.To, ct, collectionIds);
            return Result<OfficeCashTurnoverReviewDto>.Success(new(request.From, request.To, eligible, eligible.Sum(x => x.NetAmount)));
        }, ct);

    /// <summary>Whether this tenant has the module on. Reads no turnover table, so it is safe on a database without the schema.</summary>
    public async Task<Result<bool>> IsAvailableAsync()
    {
        if (!currentUser.IsAuthenticated) return Result<bool>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin")) return Result<bool>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId) return Result<bool>.Forbidden();
        return await Task.FromResult(Result<bool>.Success(options?.Allows(tenantId) == true));
    }

    public Task<Result<OfficeCashPositionDto>> GetPositionAsync(DateOnly from, DateOnly to, CancellationToken ct = default,
        IReadOnlyList<Guid>? collectionIds = null, IReadOnlyList<Guid>? turnoverIds = null) =>
        Run<OfficeCashPositionDto>(false, async actor =>
        {
            if (Validate(from, to) is { } problem) return Result<OfficeCashPositionDto>.Failure(problem, ResultStatus.Invalid);
            var collections = await db.Collections.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.CollectorId == null && x.BusinessDate >= from && x.BusinessDate <= to
                    && (collectionIds == null || collectionIds.Contains(x.Id)))
                .Select(x => new { x.Id, x.TotalAmount }).ToListAsync(ct);
            var ids = collections.Select(x => x.Id).ToArray();
            var effects = ids.Length == 0 ? new Dictionary<Guid, decimal>() : await db.CollectionCorrections.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && ids.Contains(x.OriginalCollectionId))
                .GroupBy(x => x.OriginalCollectionId).Select(g => new { Id = g.Key, Effect = g.Sum(x => x.FinancialEffectAmount) }).ToDictionaryAsync(x => x.Id, x => x.Effect, ct);
            var covered = ids.Length == 0 ? new Dictionary<Guid, decimal>() : await db.OfficeCashTurnoverCoverages.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.IsActive && ids.Contains(x.CollectionId))
                .ToDictionaryAsync(x => x.CollectionId, x => x.CoveredAmount, ct);
            var net = collections.Select(x => new { x.Id, Net = x.TotalAmount + effects.GetValueOrDefault(x.Id) }).Where(x => x.Net > 0m).ToList();
            var pending = net.Where(x => !covered.ContainsKey(x.Id)).ToList();
            var turnovers = await db.OfficeCashTurnovers.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.Status == OfficeCashTurnoverStatus.Recorded && x.TurnoverDate >= from && x.TurnoverDate <= to
                    && (turnoverIds == null || turnoverIds.Contains(x.Id)))
                .Select(x => new { x.TurnedOverAmount, x.DifferenceAmount }).ToListAsync(ct);
            return Result<OfficeCashPositionDto>.Success(new(from, to, net.Sum(x => x.Net), net.Where(x => covered.ContainsKey(x.Id)).Sum(x => covered[x.Id]),
                pending.Sum(x => x.Net), net.Count, pending.Count, turnovers.Sum(x => x.TurnedOverAmount), turnovers.Sum(x => x.DifferenceAmount),
                turnovers.Count(x => x.DifferenceAmount != 0m)));
        }, ct);

    public Task<Result<IReadOnlyList<OfficeCashTurnoverRowDto>>> GetRegisterAsync(DateOnly from, DateOnly to, CancellationToken ct = default,
        IReadOnlyList<Guid>? turnoverIds = null) =>
        Run<IReadOnlyList<OfficeCashTurnoverRowDto>>(false, async actor =>
        {
            if (Validate(from, to) is { } problem) return Result<IReadOnlyList<OfficeCashTurnoverRowDto>>.Failure(problem, ResultStatus.Invalid);
            var rows = await db.OfficeCashTurnovers.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.TurnoverDate >= from && x.TurnoverDate <= to
                    && (turnoverIds == null || turnoverIds.Contains(x.Id)))
                .OrderByDescending(x => x.RecordedAtUtc).ToListAsync(ct);
            return Result<IReadOnlyList<OfficeCashTurnoverRowDto>>.Success(rows.Select(ToRow).ToList());
        }, ct);

    public Task<Result<OfficeCashTurnoverDetailDto>> GetDetailAsync(Guid id, CancellationToken ct = default) =>
        Run<OfficeCashTurnoverDetailDto>(false, actor => DetailAsync(actor.TenantId, id, ct), ct);

    // ── Writes (Head only) ─────────────────────────────────────────────────────────────────────────────

    public Task<Result<OfficeCashTurnoverDetailDto>> RecordAsync(RecordOfficeCashTurnoverRequest request, CancellationToken ct = default) =>
        Run<OfficeCashTurnoverDetailDto>(true, async actor =>
        {
            static Result<OfficeCashTurnoverDetailDto> Fail(string error, ResultStatus status) => Result<OfficeCashTurnoverDetailDto>.Failure(error, status);
            if (request.ClientOperationId == Guid.Empty) return Fail("A valid ClientOperationId is required.", ResultStatus.Invalid);
            var fingerprint = Fingerprint(actor, request);
            var prior = await db.OfficeCashTurnovers.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.ClientOperationId == request.ClientOperationId, ct);
            if (prior is not null)
                return prior.IntentFingerprint == fingerprint
                    ? await DetailAsync(actor.TenantId, prior.Id, ct)
                    : Fail("OperationIntentConflict: this ClientOperationId is already bound to a different turnover.", ResultStatus.Conflict);

            if (Validate(request.From, request.To) is { } problem) return Fail(problem, ResultStatus.Invalid);
            if (request.TurnoverDate > BusinessToday) return Fail("A turnover cannot be dated in the future.", ResultStatus.Invalid);
            var wanted = request.CollectionIds;
            if (wanted is null || wanted.Count == 0 || wanted.Count > MaximumCollections)
                return Fail($"Select between 1 and {MaximumCollections} Office collections.", ResultStatus.Invalid);
            if (wanted.Distinct().Count() != wanted.Count || wanted.Contains(Guid.Empty))
                return Fail("Each selected collection must be distinct.", ResultStatus.Invalid);
            if (request.AmountTurnedOver <= 0m || decimal.Round(request.AmountTurnedOver, 2) != request.AmountTurnedOver
                || request.AmountTurnedOver > Collection.MaximumMoneyAmount)
                return Fail("Enter the turned-over amount in whole centavos.", ResultStatus.Invalid);

            var eligible = await EligibleAsync(actor.TenantId, request.From, request.To, ct);
            var chosen = eligible.Where(x => wanted.Contains(x.CollectionId)).ToList();
            if (chosen.Count != wanted.Count)
                return Fail("A selected collection is not available: it is not an Office collection, is outside the period, has no cash left after corrections, or is already covered by a turnover.", ResultStatus.Conflict);

            OfficeCashTurnover turnover;
            try
            {
                turnover = OfficeCashTurnover.Record(actor.TenantId, request.TurnoverDate, request.From, request.To,
                    chosen.Sum(x => x.NetAmount), request.AmountTurnedOver, chosen.Count, request.Reference, request.TurnedOverBy!,
                    request.Remarks, request.ClientOperationId, fingerprint, actor.Username, actor.ActorId, UtcNow);
            }
            catch (ArgumentException ex) { return Fail(ex.Message, ResultStatus.Invalid); }

            var actors = await db.Collections.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && wanted.Contains(x.Id))
                .Select(x => new { x.Id, x.ActorId, x.ActorName }).ToDictionaryAsync(x => x.Id, ct);
            db.OfficeCashTurnovers.Add(turnover);
            foreach (var collection in chosen)
            {
                var original = actors[collection.CollectionId];
                db.OfficeCashTurnoverCoverages.Add(OfficeCashTurnoverCoverage.Cover(actor.TenantId, turnover.Id, collection.CollectionId,
                    collection.NetAmount, original.ActorId, original.ActorName));
            }
            try { await db.SaveChangesAsync(ct); }
            catch (Exception ex) when (IsCompetingWrite(ex))
            {
                db.ChangeTracker.Clear();
                // Either a concurrent retry with this identity won (same outcome), or another turnover took one of these collections.
                var winner = await db.OfficeCashTurnovers.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.MunicipalityId == actor.TenantId && x.ClientOperationId == request.ClientOperationId, ct);
                return winner is not null && winner.IntentFingerprint == fingerprint
                    ? await DetailAsync(actor.TenantId, winner.Id, ct)
                    : Fail("A collection was covered by another turnover while this one was being saved. Review again.", ResultStatus.Conflict);
            }
            return await DetailAsync(actor.TenantId, turnover.Id, ct);
        }, ct);

    public Task<Result<OfficeCashTurnoverDetailDto>> VoidAsync(Guid id, VoidOfficeCashTurnoverRequest request, CancellationToken ct = default) =>
        Run<OfficeCashTurnoverDetailDto>(true, async actor =>
        {
            var turnover = await db.OfficeCashTurnovers.SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.Id == id, ct);
            if (turnover is null) return Result<OfficeCashTurnoverDetailDto>.NotFound();
            if (turnover.Status == OfficeCashTurnoverStatus.Voided)
                return Result<OfficeCashTurnoverDetailDto>.Failure("This turnover is already voided.", ResultStatus.Conflict);
            try { turnover.Void(request.Reason, actor.Username, actor.ActorId, UtcNow); }
            catch (ArgumentException ex) { return Result<OfficeCashTurnoverDetailDto>.Failure(ex.Message, ResultStatus.Invalid); }
            // Voiding frees the covered Collections. The turnover and its coverage rows stay as history; nothing is deleted
            // and no Collection is touched.
            foreach (var coverage in await db.OfficeCashTurnoverCoverages.Where(x => x.MunicipalityId == actor.TenantId && x.TurnoverId == id).ToListAsync(ct))
                coverage.Release();
            await db.SaveChangesAsync(ct);
            return await DetailAsync(actor.TenantId, id, ct);
        }, ct);

    // ── Internals ──────────────────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<OfficeCashCollectionDto>> EligibleAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct,
        IReadOnlyList<Guid>? collectionIds = null)
    {
        var collections = await db.Collections.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.CollectorId == null && x.BusinessDate >= from && x.BusinessDate <= to
                && (collectionIds == null || collectionIds.Contains(x.Id)))
            .Select(x => new { x.Id, x.ReferenceCode, x.BusinessDate, x.PayerName, x.TotalAmount, x.ActorName }).ToListAsync(ct);
        if (collections.Count == 0) return [];
        var ids = collections.Select(x => x.Id).ToArray();
        var effects = await db.CollectionCorrections.AsNoTracking().Where(x => x.MunicipalityId == tenantId && ids.Contains(x.OriginalCollectionId))
            .GroupBy(x => x.OriginalCollectionId).Select(g => new { Id = g.Key, Effect = g.Sum(x => x.FinancialEffectAmount) }).ToDictionaryAsync(x => x.Id, x => x.Effect, ct);
        var covered = (await db.OfficeCashTurnoverCoverages.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.IsActive && ids.Contains(x.CollectionId))
            .Select(x => x.CollectionId).ToListAsync(ct)).ToHashSet();
        return collections.Where(x => !covered.Contains(x.Id))
            .Select(x => new OfficeCashCollectionDto(x.Id, x.ReferenceCode, x.BusinessDate, x.PayerName, x.TotalAmount,
                effects.GetValueOrDefault(x.Id), x.TotalAmount + effects.GetValueOrDefault(x.Id), x.ActorName))
            .Where(x => x.NetAmount > 0m).OrderBy(x => x.BusinessDate).ThenBy(x => x.ReferenceCode).ToList();
    }

    private async Task<Result<OfficeCashTurnoverDetailDto>> DetailAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var turnover = await db.OfficeCashTurnovers.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == id, ct);
        if (turnover is null) return Result<OfficeCashTurnoverDetailDto>.NotFound();
        var coverages = await db.OfficeCashTurnoverCoverages.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.TurnoverId == id).ToListAsync(ct);
        var ids = coverages.Select(x => x.CollectionId).ToArray();
        var codes = await db.Collections.AsNoTracking().Where(x => x.MunicipalityId == tenantId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.ReferenceCode }).ToDictionaryAsync(x => x.Id, x => x.ReferenceCode, ct);
        return Result<OfficeCashTurnoverDetailDto>.Success(new(ToRow(turnover), turnover.Remarks,
            coverages.OrderBy(x => codes.GetValueOrDefault(x.CollectionId)).Select(x => new OfficeCashTurnoverCollectionDto(
                x.CollectionId, codes.GetValueOrDefault(x.CollectionId) ?? string.Empty, x.CoveredAmount, x.OriginalActorId, x.OriginalActorName, x.IsActive)).ToList()));
    }

    private static OfficeCashTurnoverRowDto ToRow(OfficeCashTurnover x) => new(x.Id, x.TurnoverDate, x.PeriodFrom, x.PeriodTo, x.ExpectedAmount,
        x.TurnedOverAmount, x.DifferenceAmount, x.NeedsReview, x.ReferenceCode, x.Reference, x.TurnedOverBy, x.Status, x.CollectionCount, x.RecordedBy,
        x.RecordedByActorId, x.RecordedAtUtc, x.VoidReason, x.VoidedBy, x.VoidedByActorId, x.VoidedAtUtc);

    private static string? Validate(DateOnly from, DateOnly to) =>
        to < from ? "The period cannot end before it begins." : to.DayNumber - from.DayNumber > 31 ? "Choose a period of at most 32 days." : null;

    private static string Fingerprint(Actor actor, RecordOfficeCashTurnoverRequest request) =>
        PostingOperation.ComputeIntentFingerprint(IntentVersion, JsonSerializer.Serialize(new
        {
            Turned = request.AmountTurnedOver.ToString("0.00", CultureInfo.InvariantCulture),
            Date = request.TurnoverDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            From = request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Collections = request.CollectionIds?.Distinct().OrderBy(x => x).ToArray(),
            Reference = request.Reference?.Trim(),
            By = request.TurnedOverBy?.Trim(),
            Remarks = request.Remarks?.Trim()
        }, JsonOptions), Origin, actor.ActorId);

    private static bool IsCompetingWrite(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is DbUpdateException) return true;
        return false;
    }

    private async Task<Result<T>> Run<T>(bool headOnly, Func<Actor, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Result<T>.Unauthorized();
        // Role before anything else: a collector is refused on every action, Admin may read, only the Head writes.
        if (currentUser.Role is not ("Admin" or "SuperAdmin") || headOnly && currentUser.Role != "SuperAdmin") return Result<T>.Forbidden();
        if (currentUser.UserId is not { } userId || userId == Guid.Empty) return Result<T>.Unauthorized();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId) return Result<T>.Forbidden();
        if (options?.Allows(tenantId) != true) return Result<T>.Failure("Office cash turnover is not enabled.", ResultStatus.Forbidden);
        try { return await action(new Actor(tenantId, currentUser.Username ?? "Head", userId.ToString("N"))); }
        catch (Exception ex) when (IsCompetingWrite(ex)) { return Result<T>.Failure("The turnover conflicts with another saved transaction.", ResultStatus.Conflict); }
    }
}
