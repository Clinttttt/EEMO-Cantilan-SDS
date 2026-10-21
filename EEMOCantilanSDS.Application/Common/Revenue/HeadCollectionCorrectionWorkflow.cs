using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Head-only (server-enforced SuperAdmin) reversal of a canonical monthly-rent receipt, deliberately narrow: an Office-recorded,
/// single-source, same-business-day, unremitted, uncorrected receipt on a Canonical rent month. The original Collection and SRC
/// are never edited; a linked negative CollectionCorrection is appended and the source balance reopens exactly once.
/// Replacements, collector cash, remitted funds, earlier days and every other source are refused, not guessed.
/// </summary>
public sealed class HeadCollectionCorrectionWorkflow(IAppDbContext db, ICollectionSessionStore store,
    ICurrentUserService user, ICurrentMunicipalityAccessor tenant, IClock clock, OfficeCashTurnoverOptions? turnover = null)
{
    private const string Origin = "HeadCollectionCorrection";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid Tenant => tenant.MunicipalityId;
    private bool InTenant => user.IsAuthenticated && Tenant != Guid.Empty
        && (!user.MunicipalityId.HasValue || user.MunicipalityId == Tenant);
    private bool IsHead => InTenant && user.Role == "SuperAdmin" && user.UserId.HasValue;
    private bool IsOffice => InTenant && user.Role is "SuperAdmin" or "Admin";
    private sealed class Rejected(string code) : Exception(code);

    private static readonly Dictionary<string, string> Explanations = new()
    {
        ["CollectionNotFound"] = "This collection was not found.",
        ["AlreadyCorrected"] = "A correction is already recorded for this collection. A collection is corrected once; the original SRC is kept.",
        ["Remitted"] = "This cash is already part of a remittance. It cannot be reversed here.",
        ["TurnedOver"] = "This Office cash is covered by an Office cash turnover. Void that turnover first if it was recorded in error.",
        ["OutsideCurrentBusinessDate"] = "Only a receipt from today's business day can be reversed here. Earlier days need an Office-approved correction procedure that is not yet defined.",
        ["CollectorRecorded"] = "A collector recorded this monthly-rent or NPM-day receipt. The collector's own current-day correction applies; the Head does not reverse it here.",
        ["SourceCorrectionUnavailable"] = "This kind of collection cannot be reversed here. Online payments, trips, attendance, penalties, fines, part payments and NPM days need their own reviewed correction.",
        ["RentSourceReconciliationRequired"] = "The rent month's source evidence needs reconciliation first.",
    };
    private static string Explain(string code) => Explanations.GetValueOrDefault(code, "This collection cannot be corrected here.");
    private static bool IsRentOnly(Collection c) => c.Lines.Count == 1 && c.Lines.Single().Allocations.Count > 0
        && c.Lines.Single().Allocations.All(a => a.SourceKind == CollectionSourceKind.PaymentRecord);

    private async Task<Collection?> LoadAsync(Guid id, CancellationToken ct) => await db.Collections
        .Include(x => x.Lines).ThenInclude(x => x.Allocations)
        .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id, ct);

    private async Task<string?> BlockAsync(Collection c, CancellationToken ct)
    {
        if (await db.CollectionCorrections.AnyAsync(x => x.MunicipalityId == Tenant && x.OriginalCollectionId == c.Id, ct)) return "AlreadyCorrected";
        if (await db.CollectionRemittanceCoverages.AnyAsync(x => x.MunicipalityId == Tenant && x.CollectionId == c.Id && x.IsActive, ct)) return "Remitted";
        // Only consulted once Office turnover is enabled, so a database without its tables is never queried.
        if (turnover?.Allows(Tenant) == true && await db.OfficeCashTurnoverCoverages.AnyAsync(x => x.MunicipalityId == Tenant && x.CollectionId == c.Id && x.IsActive, ct)) return "TurnedOver";
        if (c.BusinessDate != clock.PhilippineToday) return "OutsideCurrentBusinessDate";
        if (IsRentOnly(c)) return c.CollectorId.HasValue ? "CollectorRecorded" : null;

        // Any other same-day, unremitted source (a direct WCF/ECF amount, a Slaughterhouse or terminal/market fee, an obligation period, an NPM day)
        // is reversed by the same append-only negative correction a collector's own correction writes - including a collector's cash, because the
        // Head answers for what the Office has recorded and a wrong direct collection must be removable (never edited, never deleted).
        // Collector rent and NPM-day cash keep the collector's own correction (their balances are collector-quoted); everything unsupported is refused.
        if (c.Lines.Count == 0 || c.Lines.Any(l => l.SourceKind is CollectionSourceKind.OnlinePaymentTransaction or CollectionSourceKind.TrmTrip
            or CollectionSourceKind.TpmAttendance or CollectionSourceKind.PenaltyDefinition or CollectionSourceKind.FineAssessment
            or CollectionSourceKind.NpmRentMonth or CollectionSourceKind.PaymentRecord or CollectionSourceKind.DailyCollection)
            || c.Lines.Any(l => l.Allocations.Any(a => a.SourceKind is CollectionSourceKind.NpmRentMonth or CollectionSourceKind.PaymentRecord or CollectionSourceKind.DailyCollection)))
            return c.CollectorId.HasValue ? "CollectorRecorded" : "SourceCorrectionUnavailable";
        if (c.Lines.Any(l => l.Allocations.Any(a => a.SourceSnapshot != null && a.SourceSnapshot.Contains("\"partPayment\":true")))) return "SourceCorrectionUnavailable";
        var classes = c.Lines.Select(l => l.RevenueClassificationId).Distinct().ToArray();
        if (await db.RevenueClassifications.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && classes.Contains(x.Id)
            && x.SemanticCode == Domain.Constants.RevenueClassificationCodes.Tabo, ct)) return "SourceCorrectionUnavailable";
        if (!await db.PostingOperations.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.CollectionId == c.Id
            && x.Status == PostingOperationStatus.Succeeded && x.Origin != Origin, ct)) return "SourceCorrectionUnavailable";
        return null;
    }

    /// <summary>Read-only: any Office user may see why a receipt is or is not correctable. Nothing is written.</summary>
    public async Task<Result<CollectionReviewDto>> ReviewAsync(Guid collectionId, CancellationToken ct = default)
    {
        if (!IsOffice) return Result<CollectionReviewDto>.Forbidden();
        var c = await LoadAsync(collectionId, ct);
        if (c is null) return Result<CollectionReviewDto>.Failure("CollectionNotFound", ResultStatus.NotFound);
        var corrected = await db.CollectionCorrections.AnyAsync(x => x.MunicipalityId == Tenant && x.OriginalCollectionId == c.Id, ct);
        var remitted = await db.CollectionRemittanceCoverages.AnyAsync(x => x.MunicipalityId == Tenant && x.CollectionId == c.Id && x.IsActive, ct);
        var block = await BlockAsync(c, ct);
        if (block is null && !IsHead) block = "HeadOnly";
        var eligibility = block is null
            ? new CollectionCorrectionEligibilityDto(true, null, "Eligible: the Head may reverse this receipt today. The month's balance reopens once and the SRC is kept.")
            : new CollectionCorrectionEligibilityDto(false, block, block == "HeadOnly" ? "Only the Head can reverse a collection." : Explain(block));
        return Result<CollectionReviewDto>.Success(new(c.Id, c.ReferenceCode, c.BusinessDate, remitted, corrected, eligibility, !c.CollectorId.HasValue));
    }

    public async Task<Result<HeadCorrectionResultDto>> ReverseAsync(HeadReverseCollectionRequest request, CancellationToken ct = default)
    {
        if (!IsHead) return Result<HeadCorrectionResultDto>.Forbidden();
        try
        {
            var reason = request.Reason?.Trim() ?? string.Empty;
            if (request.ClientOperationId == Guid.Empty) throw new Rejected("InvalidClientOperationId");
            if (request.CollectionId == Guid.Empty) throw new Rejected("CollectionNotFound");
            if (reason.Length is 0 or > 500) throw new Rejected("InvalidReason");
            var normalized = JsonSerializer.Serialize(new { request.CollectionId, Reason = reason }, Json);
            var actor = user.UserId!.Value.ToString("N");
            if (await ReplayAsync(request.ClientOperationId, normalized, actor, ct) is { } prior) return Result<HeadCorrectionResultDto>.Success(prior);

            HeadCorrectionResultDto? output = null;
            await store.ExecuteAtomicallyAsync(async () =>
            {
                if (await ReplayAsync(request.ClientOperationId, normalized, actor, ct) is { } raced) { output = raced; return; }
                var c = await LoadAsync(request.CollectionId, ct) ?? throw new Rejected("CollectionNotFound");
                if (await BlockAsync(c, ct) is { } block) throw new Rejected(block);

                var correction = CollectionCorrection.Record(Tenant, c.Id, null, null, null, CollectionCorrectionType.Reversal,
                    clock.PhilippineToday, DateTime.UtcNow, -c.TotalAmount, reason, actor, user.Username ?? "Head",
                    c.Lines.Select(l => new CollectionCorrectionLineDraft(l.Id, -l.Amount,
                        l.Allocations.Select(a => new CollectionCorrectionAllocationDraft(a.Id, -a.Amount)).ToArray())));
                db.CollectionCorrections.Add(correction);
                // Only a monthly-rent allocation has a stored month projection to recompute; a utility, fee or obligation line's balance is derived
                // from its allocations net of this correction, so there is nothing further to write for it.
                foreach (var rent in c.Lines.SelectMany(x => x.Allocations).Where(x => x.SourceKind == CollectionSourceKind.PaymentRecord).GroupBy(x => x.SourceId))
                {
                    var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadByRecordIdAsync(Tenant, rent.Key, clock.PhilippineToday, true, ct);
                    if (facts?.Record is not { } record || facts.Quote.RequiresLegacyReconciliation
                        || record.SettlementAuthorityState != SettlementAuthority.Canonical)
                        throw new Rejected("RentSourceReconciliationRequired");
                    record.ApplyCanonicalRentProjection(facts.Quote.CumulativeSettledEvidence - rent.Sum(x => x.Amount),
                        correction.RecordedAtUtc, user.Username ?? "Head");
                }
                output = new(correction.Id, c.Id, c.ReferenceCode, c.TotalAmount, false);
                db.PostingOperations.Add(PostingOperation.Record(Tenant, request.ClientOperationId, 1, normalized, Origin, actor,
                    PostingOperationStatus.Succeeded, null, JsonSerializer.Serialize(output, Json), c.Id, null, DateTime.UtcNow));
                await db.SaveChangesAsync(ct);
            }, ct);
            return Result<HeadCorrectionResultDto>.Success(output!);
        }
        catch (Rejected e) { return Result<HeadCorrectionResultDto>.Failure(e.Message, ResultStatus.Conflict); }
        catch (CollectionSessionConcurrencyException) { return Result<HeadCorrectionResultDto>.Failure("ConcurrentCorrection", ResultStatus.Conflict); }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task<HeadCorrectionResultDto?> ReplayAsync(Guid operation, string normalized, string actor, CancellationToken ct)
    {
        var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == operation, ct);
        if (prior is null) return null;
        if (prior.IntentFingerprint != PostingOperation.ComputeIntentFingerprint(1, normalized, Origin, actor)) throw new Rejected("CorrectionIntentConflict");
        return JsonSerializer.Deserialize<HeadCorrectionResultDto>(prior.OutcomeDetails!, Json)! with { ExistingOutcome = true };
    }
}
