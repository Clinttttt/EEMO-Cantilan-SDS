using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The canonical money side of the NPM daily stall fee (IA-051 / IA-062). The NPM handlers stay the one place the market's
/// rules live (which day may be settled, at what fee, the month's ceiling, the month-end adjustment); this class only turns
/// the days they have just settled into ONE canonical Collection with an SRC and hands each day row to canonical authority.
///
/// <para>From the business date the Head turns the NPM daily switch on, every new daily stall-fee payment - Collector or
/// office - is posted here, so a payment is never both a legacy row's money and a Collection. Earlier rows stay legacy,
/// untouched. The classification is the existing stall-rent one; no physical serial is used. Fish and Meat weighing stay on
/// the day row under Weight and Measure and are never folded into this Collection.</para>
/// </summary>
public sealed class NpmDailyCanonicalPoster(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    GovernedCanonicalAuthority authority)
{
    public const string Origin = "NpmDaily";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>One day row and the stall-fee money just charged to it (its fee, or a month-end adjustment added to it).</summary>
    public sealed record Charge(DailyCollection Day, decimal Amount, bool AdjustmentOnly = false,
        DateTime? OriginalUpdatedAt = null, string? OriginalUpdatedBy = null);

    public sealed record Outcome(Guid CollectionId, string ReferenceCode, decimal Amount, bool Existing);

    public bool HasActiveTransaction => db.HasActiveTransaction;
    public Task<IAppDbContextTransaction> BeginSettlementTransactionAsync(CancellationToken ct = default) =>
        db.BeginSerializableTransactionAsync(ct);

    public async Task<RevenueInstrumentType?> GetInstrumentAsync(DateOnly paymentDate, CancellationToken ct = default)
    {
        var tenantId = municipality.MunicipalityId;
        var classificationId = await db.RevenueClassifications.Where(c => c.MunicipalityId == tenantId && c.IsActive
            && c.SemanticCode == RevenueClassificationCodes.PermanentStallRent).Select(c => c.Id).SingleOrDefaultAsync(ct);
        return await db.RevenueClassificationPolicies.Where(p => p.MunicipalityId == tenantId
            && p.RevenueClassificationId == classificationId && p.BusinessContext == RevenuePolicyContext.Default && p.EffectiveDate <= paymentDate)
            .OrderByDescending(p => p.EffectiveDate).Select(p => p.PermittedInstrumentType).FirstOrDefaultAsync(ct);
    }

    /// <summary>True when a payment received on this business date is canonical money.</summary>
    public Task<bool> IsCanonicalAsync(DateOnly paymentDate, CancellationToken ct = default) =>
        authority.IsCanonicalForCollectorAsync(CollectorOperationCodes.NpmDaily, paymentDate, ct);

    /// <summary>The Collection a ClientOperationId posted, for telling the device its SRC. Null when it posted none.</summary>
    public async Task<Outcome?> FindPostedAsync(Guid clientOperationId, CancellationToken ct = default)
    {
        var tenantId = municipality.MunicipalityId;
        var collectionId = await db.PostingOperations.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.ClientOperationId == clientOperationId && x.Origin == Origin
                && x.Status == PostingOperationStatus.Succeeded)
            .Select(x => x.CollectionId).SingleOrDefaultAsync(ct);
        if (collectionId is not { } id) return null;
        var collection = await db.Collections.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == id, ct);
        return collection is null ? null : new Outcome(collection.Id, collection.ReferenceCode, collection.TotalAmount, true);
    }

    /// <summary>The intent a ClientOperationId is bound to: who, which stall, which days, on which payment date.</summary>
    public string NormalizeIntent(string operation, Guid stallId, IEnumerable<DateOnly> days, DateOnly paymentDate, string? detail = null) =>
        JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            OperationType = operation,
            TenantId = municipality.MunicipalityId,
            ActorId = ActorId,
            StallId = stallId,
            Days = days.OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            PaymentDate = paymentDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Detail = detail
        }, JsonOptions);

    private string ActorId => (currentUser.CollectorId ?? currentUser.UserId ?? Guid.Empty).ToString("N");

    /// <summary>
    /// A retry of an operation already decided. Null when the id is new; the same Collection and SRC when the intent is the
    /// same; a conflict when the id was bound to something else. Asked BEFORE any market rule runs, so a replay never
    /// re-evaluates (and never re-charges) a day.
    /// </summary>
    public async Task<Result<Outcome>?> FindPriorAsync(Guid clientOperationId, string normalizedIntent, CancellationToken ct = default)
    {
        var tenantId = municipality.MunicipalityId;
        var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.ClientOperationId == clientOperationId, ct);
        if (prior is null) return null;
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalizedIntent, Origin, ActorId);
        if (prior.Origin != Origin || prior.ActorId != ActorId || prior.IntentFingerprint != fingerprint)
            return Result<Outcome>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different posting intent.", ResultStatus.Conflict);
        if (prior.Status != PostingOperationStatus.Succeeded || prior.CollectionId is not { } collectionId)
            return Result<Outcome>.Failure(prior.OutcomeDetails ?? prior.OutcomeCode ?? "The original posting was rejected.", ResultStatus.Conflict);
        var collection = await db.Collections.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectionId, ct);
        return collection is null
            ? Result<Outcome>.Failure("The recorded Collection outcome is unavailable.", ResultStatus.Conflict)
            : Result<Outcome>.Success(new Outcome(collection.Id, collection.ReferenceCode, collection.TotalAmount, true));
    }

    /// <summary>
    /// Posts one Collection for the stall-fee money the caller has just charged, one allocation per day row, and hands those
    /// rows to canonical authority. It saves the whole unit of work (the day rows and the Collection together, or neither).
    /// </summary>
    public async Task<Result<Outcome>> PostAsync(
        Stall stall, IReadOnlyList<Charge> charges, Guid clientOperationId, string normalizedIntent, DateOnly paymentDate,
        CancellationToken ct = default)
    {
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || charges.Count == 0 || charges.Any(x => x.Amount <= 0m))
            return Discard("There is no stall-fee money to post.", ResultStatus.Invalid);

        var classification = await db.RevenueClassifications.SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.SemanticCode == RevenueClassificationCodes.PermanentStallRent && x.IsActive, ct);
        var policy = classification is null ? null : await db.RevenueClassificationPolicies.Where(x =>
                x.MunicipalityId == tenantId && x.RevenueClassificationId == classification.Id
                && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= paymentDate)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        if (classification is null || policy?.PermittedInstrumentType is null)
            return Discard("No effective approved instrument policy exists for the stall-rent classification on this date.", ResultStatus.Conflict);

        var role = currentUser.Role ?? "Admin";
        var actorName = currentUser.Username ?? "System";
        var occupant = stall.ResolveOccupancy(null, paymentDate);
        var payer = string.IsNullOrWhiteSpace(occupant?.Occupant) ? null : occupant!.Occupant;
        var total = charges.Sum(x => x.Amount);
        var allocations = charges.Select(x => new CollectionAllocationDraft(CollectionSourceKind.DailyCollection, x.Day.Id, x.Amount, CollectionSourcePart.DailyFee,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1, stallId = stall.Id, stallNo = stall.StallNo, section = stall.Section?.ToString(),
                day = x.Day.CollectionDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                dailyFee = x.Day.DailyFee, monthEndAdjustment = x.Day.MonthEndAdjustment, charged = x.Amount
            }, JsonOptions))).ToList();
        var lineSnapshot = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, lineKind = "NpmDailyStallFee", facility = FacilityCode.NPM.ToString(), stallId = stall.Id,
            stallNo = stall.StallNo, days = charges.Count, policyId = policy.Id, instrument = policy.PermittedInstrumentType
        }, JsonOptions);
        var line = new CollectionLineDraft(classification, policy, total, null, null, null, lineSnapshot, allocations);

        Collection collection;
        try
        {
            collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                tenantId, clientOperationId, IntentVersion, normalizedIntent, Origin, ActorId, actorName, role, paymentDate, actorName,
                [line], null, collectorId: currentUser.CollectorId, payerName: payer,
                // A day already paid by an earlier Collection (it now carries a month-end adjustment) keeps that Collection as its payer.
                beforeCommit: (collectionId, _) =>
                {
                    foreach (var charge in charges)
                    {
                        if (charge.AdjustmentOnly)
                            charge.Day.ApplyCanonicalAdjustment(collectionId, charge.OriginalUpdatedAt, charge.OriginalUpdatedBy);
                        else if (charge.Day.CanonicalCollectionId is null) charge.Day.ApplyCanonicalPayment(collectionId);
                    }
                },
                ct: ct);
        }
        catch (DbUpdateException)
        {
            // Two requests for the same stall/day, or a retried id that won the race: nothing of this attempt was saved.
            db.ChangeTracker.Clear();
            if (await FindPriorAsync(clientOperationId, normalizedIntent, ct) is { } prior) return prior;
            return Result<Outcome>.Failure("This stall day was collected by another request. Refresh before trying again.", ResultStatus.Conflict);
        }
        return Result<Outcome>.Success(new Outcome(collection.Id, collection.ReferenceCode, collection.TotalAmount, false));
    }

    // A refused post must leave nothing behind: the caller has already marked the day rows in this unit of work, and an
    // offline sync replays several operations in one scope, so unsaved changes are dropped rather than left to ride on a later save.
    private Result<Outcome> Discard(string message, ResultStatus status)
    {
        db.ChangeTracker.Clear();
        return Result<Outcome>.Failure(message, status);
    }

    /// <summary>
    /// Voids a posted NPM daily Collection through the canonical correction record (never by editing the Collection or its
    /// amount). The correction removes its full financial effect, and each day it paid is projected unpaid, so the month's
    /// settlement owes those days again. Office only.
    /// </summary>
    public async Task<Result<Outcome>> VoidAsync(Guid collectionId, string reason, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role is not ("SuperAdmin" or "Admin"))
            return Result<Outcome>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<Outcome>.Forbidden();
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result<Outcome>.Failure("A reason of at most 500 characters is required.", ResultStatus.Invalid);

        var collection = await db.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectionId, ct);
        if (collection is null) return Result<Outcome>.NotFound();
        var allocations = collection.Lines.SelectMany(l => l.Allocations).ToList();
        if (allocations.Count == 0 || allocations.Any(a => a.SourceKind != CollectionSourceKind.DailyCollection))
            return Result<Outcome>.Failure("Only an NPM daily stall-fee Collection can be voided here.", ResultStatus.Invalid);
        if (await db.CollectionCorrections.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenantId && x.OriginalCollectionId == collectionId, ct))
            return Result<Outcome>.Failure("This Collection has already been corrected.", ResultStatus.Conflict);

        var dayIds = allocations.Select(a => a.SourceId).Distinct().ToList();
        var days = await db.DailyCollections.Where(x => x.MunicipalityId == tenantId && dayIds.Contains(x.Id)).ToListAsync(ct);
        if (days.Any(d => d.CanonicalCollectionId == collection.Id && d.CanonicalAdjustmentCollectionId is not null))
            return Result<Outcome>.Failure("Void the separately recorded month-end adjustment before its original installment.", ResultStatus.Conflict);
        var actor = currentUser.Username ?? "office";
        var lines = collection.Lines.Select(l => new CollectionCorrectionLineDraft(l.Id, -l.Amount,
            l.Allocations.Select(a => new CollectionCorrectionAllocationDraft(a.Id, -a.Amount)).ToList())).ToList();
        db.CollectionCorrections.Add(CollectionCorrection.Record(tenantId, collection.Id, null, null, null, CollectionCorrectionType.Void,
            PhilippineTime.Today, DateTime.UtcNow, -collection.TotalAmount, reason.Trim(),
            (currentUser.UserId ?? Guid.Empty).ToString("N"), actor, lines));
        // Only a day this Collection still pays is projected unpaid; a day since paid again by another Collection is left alone.
        foreach (var day in days.Where(d => d.CanonicalCollectionId == collection.Id))
            day.ApplyCanonicalVoid(actor);
        foreach (var day in days.Where(d => d.CanonicalAdjustmentCollectionId == collection.Id))
            day.ApplyCanonicalAdjustmentVoid();
        await db.SaveChangesAsync(ct);
        return Result<Outcome>.Success(new Outcome(collection.Id, collection.ReferenceCode, 0m, false));
    }
}
