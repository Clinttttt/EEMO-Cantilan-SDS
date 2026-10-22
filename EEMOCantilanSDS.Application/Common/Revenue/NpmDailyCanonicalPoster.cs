using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Common.Payments;
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
    GovernedCanonicalAuthority authority, NpmRentMonthWorkflow? monthly = null)
{
    public const string Origin = "NpmDaily";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>One day row and the stall-fee money just charged to it (its fee, or a month-end adjustment added to it).</summary>
    public sealed record Charge(DailyCollection Day, decimal Amount, bool AdjustmentOnly = false,
        DateTime? OriginalUpdatedAt = null, string? OriginalUpdatedBy = null, bool PartPayment = false);

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

    /// <summary>Replay recorded daily money using its original payment date across a later source cutover.</summary>
    public async Task<Result<Outcome>?> FindHistoricalDailyReplayAsync(Guid operation, Guid stallId, DateOnly day,
        bool paid, bool absent, CancellationToken ct = default, string? officialReceiptNumber = null)
    {
        var original = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == municipality.MunicipalityId
            && x.ClientOperationId == operation && x.Origin == Origin, ct);
        if (original is null) return null;
        try
        {
            using var document = JsonDocument.Parse(original.NormalizedIntent);
            var date = DateOnly.ParseExact(document.RootElement.GetProperty("paymentDate").GetString()!, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture);
            return await FindPriorAsync(operation, NormalizeIntent("NpmDailyRecord", stallId, [day], date,
                $"paid={paid};absent={absent}" + NpmReceiptEvidence.IntentSuffix(officialReceiptNumber)), ct);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        { return Result<Outcome>.Failure("RECONCILIATION_REQUIRED: Original daily intent cannot be verified.", ResultStatus.Conflict); }
    }

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
        CancellationToken ct = default, string? officialReceiptNumber = null)
        => await PostCombinedAsync(stall, charges, clientOperationId, normalizedIntent, paymentDate, [], null, null, null, ct, officialReceiptNumber: officialReceiptNumber);

    internal async Task<Result<Outcome>> PostCombinedAsync(Stall stall, IReadOnlyList<Charge> charges,
        Guid clientOperationId, string normalizedIntent, DateOnly paymentDate, IReadOnlyList<CollectionLineDraft> monthLines,
        NpmRentMonthPostingPermit? permit, Action<Guid, DateTime>? monthChanges, string? responsiblePayor, CancellationToken ct, IClock? postingClock = null,
        string? officialReceiptNumber = null)
    {
        if (monthly is not null)
            foreach (var period in charges.Select(c => new DateOnly(c.Day.CollectionDate.Year, c.Day.CollectionDate.Month, 1)).Distinct())
                if (await monthly.MustUseMonthlyAuthorityAsync(stall.Id, period, ct))
                    return Discard("This month uses the coordinated month writer; legacy daily posting is unavailable.", ResultStatus.Conflict);
        if (monthly is not null)
            foreach (var period in charges.Select(x => new DateOnly(x.Day.CollectionDate.Year, x.Day.CollectionDate.Month, 1)).Distinct())
                if (await monthly.IsMonthActivatedAsync(stall.Id, period, ct))
                    return Discard("Activated month authority cannot fall back to legacy daily posting.", ResultStatus.Conflict);
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || charges.Count + monthLines.Count == 0 || charges.Any(x => x.Amount <= 0m))
            return Discard("There is no stall-fee money to post.", ResultStatus.Invalid);

        var classification = await db.RevenueClassifications.SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.SemanticCode == RevenueClassificationCodes.PermanentStallRent && x.IsActive, ct);
        var policy = classification is null ? null : await db.RevenueClassificationPolicies.Where(x =>
                x.MunicipalityId == tenantId && x.RevenueClassificationId == classification.Id
                && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= paymentDate)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        if (classification is null || policy?.PermittedInstrumentType is null)
            return Discard("No effective approved instrument policy exists for the stall-rent classification on this date.", ResultStatus.Conflict);
        var receipt = await NpmReceiptEvidence.ResolveAsync(db, currentUser, tenantId, policy.PermittedInstrumentType.Value, officialReceiptNumber, ct);
        if (!receipt.IsSuccess) return Discard(receipt.Error!, receipt.Status);

        var role = currentUser.Role ?? "Admin";
        var actorName = currentUser.Username ?? "System";
        var occupant = stall.ResolveOccupancy(null, paymentDate);
        var payer = responsiblePayor ?? (string.IsNullOrWhiteSpace(occupant?.Occupant) ? null : occupant!.Occupant);
        var postingLines = monthLines.ToList();
        // Separate period lines keep receipt/report period labels accurate without splitting the actual cash receipt.
        foreach (var periodCharges in charges.GroupBy(x => new DateOnly(x.Day.CollectionDate.Year, x.Day.CollectionDate.Month, 1)))
        {
        var allocations = periodCharges.Select(x => new CollectionAllocationDraft(CollectionSourceKind.DailyCollection, x.Day.Id, x.Amount, CollectionSourcePart.DailyFee,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1, stallId = stall.Id, stallNo = stall.StallNo, section = stall.Section?.ToString(),
                contractId = StallOccupancy.AnsweringForMonth(stall.Occupancies(paymentDate), x.Day.CollectionDate.Year, x.Day.CollectionDate.Month)?.Contract.Id,
                periodStart = new DateOnly(x.Day.CollectionDate.Year, x.Day.CollectionDate.Month, 1),
                day = x.Day.CollectionDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                dailyFee = x.Day.DailyFee, monthEndAdjustment = x.Day.MonthEndAdjustment, charged = x.Amount,
                partPayment = x.PartPayment ? true : (bool?)null, settledBefore = x.PartPayment ? x.Day.CanonicalSettledAmount : (decimal?)null
            }, JsonOptions))).ToList();
        var lineSnapshot = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, lineKind = "NpmDailyStallFee", facility = FacilityCode.NPM.ToString(), stallId = stall.Id,
            stallNo = stall.StallNo, periodStart = periodCharges.Key, days = periodCharges.Count(), policyId = policy.Id, instrument = policy.PermittedInstrumentType
        }, JsonOptions);
        postingLines.Add(new CollectionLineDraft(classification, policy, periodCharges.Sum(x => x.Amount), null, null, null, lineSnapshot, allocations));
        }

        Collection collection;
        try
        {
            collection = await new CanonicalCollectionPostingCoordinator(db, postingClock).PostAsync(
                tenantId, clientOperationId, IntentVersion, normalizedIntent, Origin, ActorId, actorName, role, paymentDate, actorName,
                postingLines, receipt.Value, collectorId: currentUser.CollectorId, payerName: payer,
                // A day already paid by an earlier Collection (it now carries a month-end adjustment) keeps that Collection as its payer.
                beforeCommit: (collectionId, recordedAt) =>
                {
                    foreach (var charge in charges)
                    {
                        if (charge.PartPayment)
                            charge.Day.ApplyCanonicalPartial(collectionId, charge.Amount, currentUser.CollectorId, actorName);
                        else if (charge.AdjustmentOnly)
                            charge.Day.ApplyCanonicalAdjustment(collectionId, charge.OriginalUpdatedAt, charge.OriginalUpdatedBy);
                        else if (charge.Day.CanonicalCollectionId is null) charge.Day.ApplyCanonicalPayment(collectionId);
                    }
                    monthChanges?.Invoke(collectionId, recordedAt);
                },
                ct: ct, npmMonthPermit: permit);
        }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException
            or InvalidOperationException { InnerException: DbUpdateException or System.Data.Common.DbException })
        {
            // Two requests for the same stall/day, or a retried id that won the race: nothing of this attempt was saved.
            db.ChangeTracker.Clear();
            // Inside a serializable transaction a lost race aborts the whole transaction, so nothing more may be read in it: the caller
            // retries, and a retried id resolves to the winner at the top of its own request.
            if (db.HasActiveTransaction)
                return Result<Outcome>.Failure("RECONCILIATION_REQUIRED: Another payment for this stall was recorded at the same time. Review the refreshed balance.", ResultStatus.Conflict);
            if (await FindPriorAsync(clientOperationId, normalizedIntent, ct) is { } prior) return prior;
            return Result<Outcome>.Failure("This stall day was collected by another request. Refresh before trying again.", ResultStatus.Conflict);
        }
        return Result<Outcome>.Success(new Outcome(collection.Id, collection.ReferenceCode, collection.TotalAmount, false));
    }

    // A refused post must leave nothing behind: the caller has already marked the day rows in this unit of work, and an
    // offline sync replays several operations in one scope, so unsaved changes are dropped rather than left to ride on a later save.
    internal Result<Outcome> DiscardStagedPayment(string message, ResultStatus status) => Discard(message, status);

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
        if (allocations.Count == 0 || allocations.Any(a => a.SourceKind is not (CollectionSourceKind.DailyCollection or CollectionSourceKind.NpmRentMonth))
            || allocations.Any(a => a.SourceKind == CollectionSourceKind.DailyCollection && a.SourcePart != CollectionSourcePart.DailyFee))
            return Result<Outcome>.Failure("Only an NPM daily stall-fee Collection can be voided here.", ResultStatus.Invalid);
        if (allocations.Any(a => a.SourceKind == CollectionSourceKind.NpmRentMonth) && monthly?.Enabled != true)
            return Result<Outcome>.Failure("The coordinated month correction workflow is unavailable.", ResultStatus.Conflict);
        if (await db.CollectionRemittanceCoverages.AnyAsync(x => x.MunicipalityId == tenantId && x.CollectionId == collectionId && x.IsActive, ct))
            return Result<Outcome>.Failure("Remitted cash requires the established remittance reconciliation process.", ResultStatus.Conflict);
        if (await db.CollectionCorrections.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenantId && x.OriginalCollectionId == collectionId, ct))
            return Result<Outcome>.Failure("This Collection has already been corrected.", ResultStatus.Conflict);

        await using var monthTransaction = monthly?.Enabled == true && !db.HasActiveTransaction
            ? await db.BeginSerializableTransactionAsync(ct) : null;
        if (monthly?.Enabled == true && await db.CollectionCorrections.AnyAsync(x => x.MunicipalityId == tenantId
            && x.OriginalCollectionId == collectionId, ct))
            return Result<Outcome>.Failure("This Collection has already been corrected.", ResultStatus.Conflict);

        var dayIds = allocations.Where(a => a.SourceKind == CollectionSourceKind.DailyCollection).Select(a => a.SourceId).Distinct().ToList();
        var days = await db.DailyCollections.Where(x => x.MunicipalityId == tenantId && dayIds.Contains(x.Id)).ToListAsync(ct);
        // A day that part payments built up takes back exactly what THIS Collection applied; the other payments stay in force.
        var partPaid = days.Where(d => d.CanonicalSettledAmount > 0m).ToDictionary(d => d.Id);
        if (allocations.Any(a => partPaid.TryGetValue(a.SourceId, out var d) && d.CanonicalAdjustmentCollectionId is { } adjustment && adjustment != collection.Id)
            || days.Any(d => d.CanonicalSettledAmount == 0m && d.CanonicalCollectionId == collection.Id && d.CanonicalAdjustmentCollectionId is not null))
            return Result<Outcome>.Failure("Void the separately recorded month-end adjustment before its original installment.", ResultStatus.Conflict);
        var actor = currentUser.Username ?? "office";
        if (monthly?.Enabled == true)
            await monthly.TouchCorrectionMonthsAsync(allocations, "Existing Head void of Collection " + collectionId + ": " + reason.Trim(), ct);
        var lines = collection.Lines.Select(l => new CollectionCorrectionLineDraft(l.Id, -l.Amount,
            l.Allocations.Select(a => new CollectionCorrectionAllocationDraft(a.Id, -a.Amount)).ToList())).ToList();
        db.CollectionCorrections.Add(CollectionCorrection.Record(tenantId, collection.Id, null, null, null, CollectionCorrectionType.Void,
            PhilippineTime.Today, DateTime.UtcNow, -collection.TotalAmount, reason.Trim(),
            (currentUser.UserId ?? Guid.Empty).ToString("N"), actor, lines));
        // Only a day this Collection still pays is projected unpaid; a day since paid again by another Collection is left alone.
        foreach (var allocation in allocations.Where(a => partPaid.TryGetValue(a.SourceId, out var d) && d.CanonicalAdjustmentCollectionId != collection.Id))
        {
            var day = partPaid[allocation.SourceId];
            // The latest other Collection still in force that paid part of this day, so the day keeps pointing at real money.
            var remaining = await (from a in db.CollectionAllocations
                                   join l in db.CollectionLines on a.CollectionLineId equals l.Id
                                   join c in db.Collections on l.CollectionId equals c.Id
                                   where a.MunicipalityId == tenantId && a.SourceKind == CollectionSourceKind.DailyCollection && a.SourceId == day.Id
                                       && c.Id != collection.Id && !db.CollectionCorrections.Any(x => x.OriginalCollectionId == c.Id)
                                   orderby c.RecordedAtUtc descending
                                   select (Guid?)c.Id).FirstOrDefaultAsync(ct);
            day.ReverseCanonicalPartial(allocation.Amount, remaining, actor);
        }
        foreach (var day in days.Where(d => d.CanonicalSettledAmount == 0m && !partPaid.ContainsKey(d.Id) && d.CanonicalCollectionId == collection.Id))
            day.ApplyCanonicalVoid(actor);
        foreach (var day in days.Where(d => d.CanonicalAdjustmentCollectionId == collection.Id))
            day.ApplyCanonicalAdjustmentVoid();
        await db.SaveChangesAsync(ct);
        if (monthTransaction is not null) await monthTransaction.CommitAsync(ct);
        return Result<Outcome>.Success(new Outcome(collection.Id, collection.ReferenceCode, 0m, false));
    }
}
