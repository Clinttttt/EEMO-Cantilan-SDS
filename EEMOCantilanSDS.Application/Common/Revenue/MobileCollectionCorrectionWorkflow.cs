using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Collector-only, current-day audited reversal/replacement. Source writers still own all replacement money.</summary>
public sealed class MobileCollectionCorrectionWorkflow(IAppDbContext db, ICollectionSessionStore store,
    ICollectionSessionSources sources, CollectionSessionWorkflow sessions, ICollectionActivityReader activity,
    OfficeCollectionWorkflow office, ICurrentUserService user, ICurrentMunicipalityAccessor tenant, IClock clock)
{
    private const string Origin = "MobileSelfCorrection";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid Tenant => tenant.MunicipalityId;
    private string Actor => user.CollectorId!.Value.ToString("N");
    private async Task<bool> Authorized(CancellationToken ct) => user.IsAuthenticated && user.Role == "Collector"
        && Tenant != Guid.Empty && (!user.MunicipalityId.HasValue || user.MunicipalityId == Tenant)
        && user.CollectorId is { } collector && await store.IsActiveCollectorAsync(Tenant, collector, ct);
    private sealed class Rejected(string code) : Exception(code);
    private static string Reason(MobileCorrectionReasonIntent? reason)
    {
        if (reason is null || !Enum.IsDefined(reason.Code) || reason.Note?.Trim().Length > 300
            || reason.Code == MobileCorrectionReason.Other && string.IsNullOrWhiteSpace(reason.Note)) throw new Rejected("InvalidReason");
        var label = reason.Code switch { MobileCorrectionReason.EnteredByMistake => "Entered by mistake", MobileCorrectionReason.WrongPayerOrSource => "Wrong payer/source",
            MobileCorrectionReason.WrongAmount => "Wrong amount", MobileCorrectionReason.Duplicate => "Duplicate", _ => "Other" };
        return string.IsNullOrWhiteSpace(reason.Note) ? label : label + ": " + reason.Note.Trim();
    }
    private sealed record CorrectionLocks(HashSet<Guid> Remitted, HashSet<Guid> Corrected, HashSet<Guid> Posted,
        HashSet<Guid> Native, HashSet<Guid> Dependent, HashSet<Guid> TaboClasses);
    private async Task<CorrectionLocks> LocksAsync(IReadOnlyList<Collection> collections, CancellationToken ct)
    {
        var ids = collections.Select(x => x.Id).ToArray();
        var classes = collections.SelectMany(x => x.Lines).Select(x => x.RevenueClassificationId).Distinct().ToArray();
        var dailyIds = collections.SelectMany(x => x.Lines).SelectMany(x => x.Allocations)
            .Where(x => x.SourceKind == CollectionSourceKind.DailyCollection).Select(x => x.SourceId).Distinct().ToArray();
        var remitted = await db.CollectionRemittanceCoverages.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.CollectionId) && x.IsActive)
            .Select(x => x.CollectionId).ToListAsync(ct);
        var corrected = await db.CollectionCorrections.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.OriginalCollectionId))
            .Select(x => x.OriginalCollectionId).ToListAsync(ct);
        var posted = await db.PostingOperations.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.CollectionId != null
            && ids.Contains(x.CollectionId.Value) && x.Status == PostingOperationStatus.Succeeded && x.Origin != Origin)
            .Select(x => new { Id = x.CollectionId!.Value, x.Origin }).ToListAsync(ct);
        var dependent = await db.DailyCollections.AsNoTracking().Where(x => x.MunicipalityId == Tenant && dailyIds.Contains(x.Id)
            && x.CanonicalCollectionId != null && ids.Contains(x.CanonicalCollectionId.Value) && x.CanonicalAdjustmentCollectionId != null)
            .Select(x => x.CanonicalCollectionId!.Value).ToListAsync(ct);
        var tabo = await db.RevenueClassifications.AsNoTracking().Where(x => x.MunicipalityId == Tenant && classes.Contains(x.Id)
            && x.SemanticCode == RevenueClassificationCodes.Tabo).Select(x => x.Id).ToListAsync(ct);
        return new(remitted.ToHashSet(), corrected.ToHashSet(), posted.Select(x => x.Id).ToHashSet(),
            posted.Where(x => x.Origin == "SourceNativeOfficeCollection").Select(x => x.Id).ToHashSet(), dependent.ToHashSet(), tabo.ToHashSet());
    }
    private async Task<string?> BlockAsync(Collection c, CancellationToken ct) => Block(c, await LocksAsync([c], ct));
    private string? Block(Collection c, CorrectionLocks locks)
    {
        if (c.CollectorId != user.CollectorId) return "DifferentCollector";
        if (c.BusinessDate != clock.PhilippineToday) return "OutsideCurrentBusinessDate";
        if (locks.Remitted.Contains(c.Id)) return "Remitted";
        if (locks.Corrected.Contains(c.Id)) return "AlreadyCorrected";
        if (c.Lines.Any(x => locks.TaboClasses.Contains(x.RevenueClassificationId))) return "SourceCorrectionUnavailable";
        if (c.Lines.Count == 0 || c.Lines.Any(l => l.SourceKind is CollectionSourceKind.OnlinePaymentTransaction or CollectionSourceKind.TrmTrip
            or CollectionSourceKind.TpmAttendance or CollectionSourceKind.PenaltyDefinition)) return "SourceCorrectionUnavailable";
        if (c.Lines.Any(l => l.SourceKind is null && (l.Allocations.Count == 0 || l.Allocations.Any(a => a.SourceKind is not
            (CollectionSourceKind.DailyCollection or CollectionSourceKind.PaymentRecord or CollectionSourceKind.UtilityBill or CollectionSourceKind.ObligationPeriod))))
            && !locks.Native.Contains(c.Id))
            return "SourceCorrectionUnavailable";
        if (locks.Dependent.Contains(c.Id)) return "LaterSettlementExists";
        if (!locks.Posted.Contains(c.Id)) return "SourceCorrectionUnavailable";
        return null;
    }
    private async Task<Collection> OriginalAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id, ct) ?? throw new Rejected("CollectionNotFound");
        if (await BlockAsync(c, ct) is { } block) throw new Rejected(block);
        return c;
    }
    private async Task<CollectionCorrection> ReverseAsync(Collection c, MobileCorrectionReasonIntent reason, CancellationToken ct)
    {
        var correction = CollectionCorrection.Record(Tenant, c.Id, null, null, null, CollectionCorrectionType.Reversal,
            clock.PhilippineToday, DateTime.UtcNow, -c.TotalAmount, Reason(reason), Actor, user.Username ?? "Collector",
            c.Lines.Select(l => new CollectionCorrectionLineDraft(l.Id, -l.Amount,
                l.Allocations.Select(a => new CollectionCorrectionAllocationDraft(a.Id, -a.Amount)).ToArray())));
        db.CollectionCorrections.Add(correction);
        var ids = c.Lines.SelectMany(x => x.Allocations).Where(x => x.SourceKind == CollectionSourceKind.DailyCollection).Select(x => x.SourceId).ToArray();
        foreach (var day in await db.DailyCollections.Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id)).ToListAsync(ct))
        {
            if (day.CanonicalCollectionId == c.Id) day.ApplyCanonicalVoid(user.Username ?? "Collector");
            if (day.CanonicalAdjustmentCollectionId == c.Id) day.ApplyCanonicalAdjustmentVoid();
        }
        await db.SaveChangesAsync(ct);
        return correction;
    }
    private static void ValidateEdit(EditMobileCollectionIntent edit, DateOnly today)
    {
        if (edit.ClientOperationId == Guid.Empty || edit.Replacement is null || edit.Replacement.BusinessDate != today
            || edit.Replacement.ClientCollectionSessionId != edit.ClientOperationId || edit.Replacement.Items?.Count != 1)
            throw new Rejected("InvalidReplacementIntent");
        Reason(edit.Reason);
    }
    public async Task<Result<MobileRecentCollections>> RecentAsync(CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<MobileRecentCollections>.Forbidden();
        var today = clock.PhilippineToday;
        var events = (await activity.GetAsync(Tenant, today, today, ct)).Where(x => x.Authority == "Canonical" && x.CollectorId == user.CollectorId)
            .OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.CollectionId).ToArray();
        var ids = events.Take(100).Select(x => x.CollectionId!.Value).ToArray();
        var originals = await db.Collections.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Allocations)
            .Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var native = (await office.ActivityAsync(today, today, ct: ct)).Value ?? [];
        var editSources = await EditSourcesAsync(originals.Values.ToArray(), native, ct);
        var locks = await LocksAsync(originals.Values.ToArray(), ct);
        var rows = new List<MobileRecentCollection>();
        foreach (var e in events.Take(100))
        {
            var block = Block(originals[e.CollectionId!.Value], locks);
            var editSource = editSources.GetValueOrDefault(e.CollectionId.Value);
            var edit = block is null && e.Lines.Count == 1 && editSource is not null;
            rows.Add(new(e, edit, block is null, block ?? (edit ? null : e.Lines.Count != 1 ? "MultipleSourceBoundaries" : "SourceCorrectionUnavailable"),
                block is null && edit ? null : "This collection is locked or requires source review.", native.FirstOrDefault(x => x.CollectionId == e.CollectionId), editSource));
        }
        return Result<MobileRecentCollections>.Success(new(today, rows, events.Length > 100));
    }
    private sealed record GovernedEditFacts(string OperationCode, Guid? FeeOptionId, string? VehicleClassCode, GovernedServiceMode? Mode, string? Reference);
    private sealed record DailyOperation(string OperationType);
    private async Task<Dictionary<Guid, MobileCollectionEditSource>> EditSourcesAsync(IReadOnlyList<Collection> collections,
        IReadOnlyList<SourceNativeActivityDto> native, CancellationToken ct)
    {
        // Bounded lookups by recorded IDs; source identity never comes from payer-name matching.
        var ids = collections.SelectMany(c => c.Lines.SelectMany(l => l.Allocations.Select(a => a.SourceId)
            .Concat(l.SourceId is { } id ? [id] : []))).Distinct().ToArray();
        var days = await db.DailyCollections.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var bills = await db.UtilityBills.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var rents = await db.PaymentRecords.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var periods = await db.ObligationPeriods.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var accountIds = periods.Values.Select(x => x.ObligationAccountId).ToArray();
        var accounts = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == Tenant && accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var stallIds = days.Values.Select(x => x.StallId).Concat(bills.Values.Select(x => x.StallId)).Concat(rents.Values.Select(x => x.StallId)).Distinct().ToArray();
        var stalls = await db.Stalls.AsNoTracking().Include(x => x.Contracts).Include(x => x.Facility)
            .Where(x => x.MunicipalityId == Tenant && stallIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var collectionIds = collections.Select(x => x.Id).ToArray();
        var operations = (await db.PostingOperations.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.CollectionId != null
            && collectionIds.Contains(x.CollectionId.Value) && x.Origin != Origin).ToListAsync(ct)).ToLookup(x => x.CollectionId!.Value);
        var result = new Dictionary<Guid, MobileCollectionEditSource>();
        foreach (var c in collections.Where(x => x.Lines.Count == 1))
        {
            try
            {
            if (native.FirstOrDefault(x => x.CollectionId == c.Id) is { } n)
            {
                result[c.Id] = new(n.VendorRegistrationId is { } vendor ? new(SourceIdentityKind.FishMeatVendorRegistration, vendor) : null,
                    CollectionSessionItemKind.SourceNative, n.OperationCode, new(VendorRegistrationId: n.VendorRegistrationId, TerminalSection: n.Section, VehicleClassId: n.VehicleClassId),
                    new(n.OperationCode, n.VendorRegistrationId, n.Section, n.VehicleClassId, n.CashTicketCount, n.Kilograms), PayerSnapshot: n.PayerSnapshot);
                continue;
            }
            var line = c.Lines.Single(); var allocation = line.Allocations.FirstOrDefault();
            var id = line.SourceId ?? allocation?.SourceId;
            if (id is null) continue;
            Guid? stallId = null; int year = c.BusinessDate.Year, month = c.BusinessDate.Month; long version = 0;
            CollectionSessionItemKind kind; string code;
            if (days.TryGetValue(id.Value, out var day))
            {
                stallId = day.StallId; year = day.CollectionDate.Year; month = day.CollectionDate.Month;
                var whole = operations[c.Id].Any(x => JsonSerializer.Deserialize<DailyOperation>(x.NormalizedIntent, Json)?.OperationType == "NpmWholePayment");
                kind = whole ? CollectionSessionItemKind.NpmWholePayment : CollectionSessionItemKind.NpmDaily;
                code = whole ? "NPM_WHOLE_PAYMENT" : CollectorOperationCodes.NpmDaily;
            }
            else if (bills.TryGetValue(id.Value, out var bill))
            {
                stallId = bill.StallId; year = bill.BillingYear; month = bill.BillingMonth;
                var water = (line.SourcePart ?? allocation?.SourcePart) == CollectionSourcePart.Water;
                kind = water ? CollectionSessionItemKind.Water : CollectionSessionItemKind.Electricity; code = water ? "WCF" : "ECF";
                version = water ? bill.WaterSourceVersion : bill.ElectricitySourceVersion;
            }
            else if (rents.TryGetValue(id.Value, out var rent))
            {
                stallId = rent.StallId; year = rent.BillingYear; month = rent.BillingMonth; version = rent.SettlementVersion;
                kind = CollectionSessionItemKind.MonthlyRent; code = "FACILITY_" + stalls[stallId.Value].Facility!.Code;
            }
            else if (periods.TryGetValue(id.Value, out var period) && accounts.TryGetValue(period.ObligationAccountId, out var account)
                && account.Kind is ObligationKind.KanmanggaySpaceRental or ObligationKind.FiestaArawLotRental)
            {
                code = account.Kind == ObligationKind.KanmanggaySpaceRental ? CollectorOperationCodes.KanmanggaySpaceRental : CollectorOperationCodes.FiestaArawLotRental;
                result[c.Id] = new(new(SourceIdentityKind.SpaceAccount, account.Id), CollectionSessionItemKind.Obligation, code,
                    new(AccountId: account.Id, Year: period.PeriodStart.Year, Month: period.PeriodStart.Month, PeriodStart: period.PeriodStart));
                continue;
            }
            else if (line.SourceKind == CollectionSourceKind.GovernedService && line.CalculationSnapshot is { } snapshot)
            {
                var facts = JsonSerializer.Deserialize<GovernedEditFacts>(snapshot, Json);
                if (facts?.OperationCode is null) continue;
                SessionSlaughterIntent? slaughter = null;
                if (facts.OperationCode == CollectorOperationCodes.Slaughterhouse)
                {
                    using var document = JsonDocument.Parse(snapshot);
                    var source = document.RootElement.GetProperty("source");
                    if (!Enum.TryParse<AnimalType>(source.GetProperty("animal").GetString(), out var animal)) continue;
                    slaughter = new(animal, source.GetProperty("heads").GetInt32(), source.GetProperty("customAnimalName").GetString(), source.GetProperty("ownerName").GetString());
                }
                result[c.Id] = new(null, slaughter is null ? CollectionSessionItemKind.GovernedService : CollectionSessionItemKind.Slaughter,
                    facts.OperationCode, new(FeeOptionId: facts.FeeOptionId, VehicleClassCode: facts.VehicleClassCode, Mode: facts.Mode),
                    Slaughter: slaughter, PayerSnapshot: c.PayerName, Reference: facts.Reference);
                continue;
            }
            else continue;
            var owner = stalls.GetValueOrDefault(stallId!.Value)?.OccupancyAnsweringForMonth(year, month, c.BusinessDate)?.Contract;
            if (owner is not null) result[c.Id] = new(new(SourceIdentityKind.Occupancy, owner.Id), kind, code,
                new(StallId: stallId, OccupancyId: owner.Id, UtilityBillId: bills.ContainsKey(id.Value) ? id : null,
                    SourceVersion: version, Year: year, Month: month, PeriodStart: new(year, month, 1)), PayerSnapshot: c.PayerName);
            }
            catch (JsonException) { /* Malformed frozen metadata blocks edit for this row, not the whole feed. */ }
            catch (KeyNotFoundException) { /* A missing source fact cannot be invented. */ }
            catch (InvalidOperationException) { /* Invalid JSON value kinds cannot authorize a replacement. */ }
        }
        return result;
    }
    public async Task<Result<CollectionSessionQuote>> QuoteEditAsync(EditMobileCollectionIntent edit, CancellationToken ct = default)
    {
        if (!await Authorized(ct)) return Result<CollectionSessionQuote>.Forbidden();
        try
        {
            ValidateEdit(edit, clock.PhilippineToday);
            await using var tx = await db.BeginSerializableTransactionAsync(ct);
            var original = await OriginalAsync(edit.CollectionId, ct);
            if (original.Lines.Count != 1) throw new Rejected("MultipleSourceBoundaries");
            await ReverseAsync(original, edit.Reason, ct);
            // A preview releases the original's settlement only inside an uncommitted transaction. No SRC is allocated.
            return await sessions.QuoteAsync(edit.Replacement, ct);
        }
        catch (Rejected e) { return Result<CollectionSessionQuote>.Failure(e.Message, ResultStatus.Conflict); }
        finally { db.ChangeTracker.Clear(); }
    }
    private async Task<MobileCollectionCorrectionResult?> Replay(Guid operation, string normalized, CancellationToken ct)
    {
        var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == operation, ct);
        if (prior is null) return null;
        if (prior.IntentFingerprint != PostingOperation.ComputeIntentFingerprint(1, normalized, Origin, Actor)) throw new Rejected("CorrectionIntentConflict");
        return JsonSerializer.Deserialize<MobileCollectionCorrectionResult>(prior.OutcomeDetails!, Json)! with { ExistingOutcome = true };
    }
    public async Task<Result<MobileCollectionCorrectionResult>> RemoveAsync(RemoveMobileCollectionRequest request, CancellationToken ct = default)
        => await ExecuteAsync(request.ClientOperationId, request.CollectionId, request.Reason, null, null, ct);
    public async Task<Result<MobileCollectionCorrectionResult>> EditAsync(RecordMobileCollectionEditRequest request, CancellationToken ct = default)
        => await ExecuteAsync(request.Intent.ClientOperationId, request.Intent.CollectionId, request.Intent.Reason, request.Intent, request.QuoteFingerprint, ct);
    private async Task<Result<MobileCollectionCorrectionResult>> ExecuteAsync(Guid operation, Guid originalId, MobileCorrectionReasonIntent reason,
        EditMobileCollectionIntent? edit, string? fingerprint, CancellationToken ct)
    {
        if (!await Authorized(ct)) return Result<MobileCollectionCorrectionResult>.Forbidden();
        try
        {
            if (operation == Guid.Empty) throw new Rejected("InvalidClientOperationId");
            var normalized = JsonSerializer.Serialize(new { OriginalId = originalId, Reason = Reason(reason), Replacement = edit?.Replacement }, Json);
            if (await Replay(operation, normalized, ct) is { } prior) return Result<MobileCollectionCorrectionResult>.Success(prior);
            MobileCollectionCorrectionResult? output = null;
            await store.ExecuteAtomicallyAsync(async () =>
            {
                if (await Replay(operation, normalized, ct) is { } raced) { output = raced; return; }
                if (await store.FindAsync(Tenant, operation, ct) is not null) throw new Rejected("CorrectionIntentConflict");
                if (edit is not null) ValidateEdit(edit, clock.PhilippineToday);
                var original = await OriginalAsync(originalId, ct);
                if (edit is not null && original.Lines.Count != 1) throw new Rejected("MultipleSourceBoundaries");
                var reversal = await ReverseAsync(original, reason, ct);
                CollectionSessionCollection? replacement = null;
                if (edit is not null)
                {
                    var quoted = await sessions.QuoteAsync(edit.Replacement, ct);
                    if (!quoted.IsSuccess || !quoted.Value!.CanRecord || quoted.Value.QuoteFingerprint != fingerprint) throw new Rejected("QuoteStale");
                    var item = edit.Replacement.Items.Single();
                    var child = CollectionSessionWorkflow.ChildOperationId(Tenant, operation, item.ClientItemId);
                    if (await db.PostingOperations.AnyAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == child, ct))
                        throw new Rejected("CorrectionIntentConflict");
                    replacement = await sources.PostAsync(edit.Replacement, item, child, ct);
                    var posted = await db.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync(x => x.MunicipalityId == Tenant && x.Id == replacement.CollectionId, ct);
                    var oldLine = original.Lines.Single();
                    // Never let an edit silently cross an operation's financial classification/boundary.
                    if (posted.Lines.Count != 1 || posted.Lines.Single().SourceKind != oldLine.SourceKind
                        || oldLine.SourceKind != CollectionSourceKind.TerminalSection && posted.Lines.Single().RevenueClassificationId != oldLine.RevenueClassificationId
                        || !posted.Lines.Single().Allocations.Select(x => x.SourceKind).Distinct().OrderBy(x => x)
                            .SequenceEqual(oldLine.Allocations.Select(x => x.SourceKind).Distinct().OrderBy(x => x)))
                        throw new Rejected("ReplacementSourceMismatch");
                    reversal.CompleteReplacement(replacement.CollectionId);
                }
                output = new(reversal.Id, original.Id, original.ReferenceCode, replacement);
                db.PostingOperations.Add(PostingOperation.Record(Tenant, operation, 1, normalized, Origin, Actor, PostingOperationStatus.Succeeded,
                    null, JsonSerializer.Serialize(output, Json), original.Id, null, DateTime.UtcNow));
                await db.SaveChangesAsync(ct);
            }, ct);
            return Result<MobileCollectionCorrectionResult>.Success(output!);
        }
        catch (Rejected e) { return Result<MobileCollectionCorrectionResult>.Failure(e.Message, ResultStatus.Conflict); }
        catch (CollectionSessionPostingException) { return Result<MobileCollectionCorrectionResult>.Failure("QuoteStale", ResultStatus.Conflict); }
        catch (CollectionSessionConcurrencyException)
        { return Result<MobileCollectionCorrectionResult>.Failure("ConcurrentCorrection", ResultStatus.Conflict); }
    }
}
