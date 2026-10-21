using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The one definition of "verified" for Collector direct collections, shared by single confirm, bulk confirm, the review list, the verification
/// position and Collection Activity so they cannot disagree. Verification is only the latest append-only <see cref="DirectCollectionReviewEvent"/>:
/// it never creates, holds, hides or changes a Collection, SRC, amount, remittance or deposit. Cash counts as received whether or not it is verified.
/// </summary>
public static class DirectCollectionVerification
{
    public const string Awaiting = "Awaiting review";
    public const string Confirmed = "Confirmed";
    public const string NeedsCorrection = "Needs correction";
    public const string Resolved = "Resolved";

    // Activity labels (a different vocabulary from the review state: these describe the cash).
    public const string LabelVerified = "Verified";
    public const string LabelAwaiting = "Awaiting verification";
    public const string LabelNeedsCorrection = "Needs correction";
    public const string LabelResolved = "Awaiting confirmation";
    public const string LabelReversed = "Reversed";

    public const int MinimumDuplicateJustification = 10;

    // Operations where money is recorded with no office-prepared amount: the source is the registered vendor, the service, the weighing,
    // the terminal section or the fine authority. A prepared obligation (NPM daily/rent, assessed periods, fine assessments) is not direct.
    private static readonly CollectionSourceKind[] DirectKinds =
    [
        CollectionSourceKind.GovernedService, CollectionSourceKind.NpmWeighing, CollectionSourceKind.FishMeatVendorFee,
        CollectionSourceKind.FishMeatVendorRegistration, CollectionSourceKind.TerminalSection, CollectionSourceKind.DirectFineCollection
    ];

    /// <summary>
    /// The actual Head Collection Review scope: directly collected utility charges and Fish/Meat
    /// payer-only collections. Other canonical receipts stay Posted and fully accountable, but
    /// their outstanding review badge must not point to a queue that deliberately excludes them.
    /// </summary>
    public static IQueryable<Collection> ReviewableDirectCollections(IAppDbContext db, Guid tenant, IQueryable<Collection> candidate)
    {
        var utility = from a in db.CollectionAllocations.AsNoTracking()
                      join l in db.CollectionLines.AsNoTracking() on a.CollectionLineId equals l.Id
                      where a.MunicipalityId == tenant && a.SourceKind == CollectionSourceKind.UtilityBill
                      select l.CollectionId;
        var payerOnly = FishMeatPayorFollowUpWorkflow.PayerOnlyCollections(db, tenant).Select(c => c.Id);
        return candidate.Where(c => utility.Contains(c.Id) || payerOnly.Contains(c.Id));
    }
    /// <summary>Collections a Collector recorded directly. Office cash, assessed obligations and online settlements are not in this set.</summary>
    public static IQueryable<Collection> DirectCollections(IAppDbContext db, Guid tenant)
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
        // A Vendor Fee recorded against a payer name only has no source kind, but it is a direct collection all the same.
        var payerOnly = FishMeatPayorFollowUpWorkflow.PayerOnlyCollections(db, tenant).Select(c => c.Id);
        return db.Collections.AsNoTracking().Where(c => c.MunicipalityId == tenant && c.CollectorId != null
            && (directLines.Contains(c.Id) || directUtility.Contains(c.Id) || payerOnly.Contains(c.Id)));
    }

    /// <summary>
    /// Why Confirm is not available, or null when it is. One rule set for single and bulk: a reversed receipt, a correction that is still
    /// open (it must be Resolved first), a collection already confirmed (a stale or duplicate action) and an unresolved required source identity
    /// are always refused. A possible duplicate is refused in bulk; singly it needs the Head's written reason it is not a duplicate.
    /// </summary>
    public static string? ConfirmBlock(string disposition, string reviewState, bool possibleDuplicate, bool needsRegistration, string? duplicateJustification)
    {
        if (disposition == "Reversed") return "Reversed";
        if (reviewState == Confirmed) return "AlreadyConfirmed";
        if (reviewState == NeedsCorrection) return "CorrectionUnresolved";
        if (reviewState is not (Awaiting or Resolved)) return "NotReviewable";
        if (needsRegistration) return "IdentityUnresolved";
        if (possibleDuplicate && (duplicateJustification?.Trim().Length ?? 0) < MinimumDuplicateJustification) return "PossibleDuplicate";
        return null;
    }

    public static string BlockMessage(string code) => code switch
    {
        "Reversed" => "This collection was reversed; there is nothing to confirm.",
        "AlreadyConfirmed" => "This collection is already confirmed. Reload to see the latest review.",
        "CorrectionUnresolved" => "A correction is still open. Resolve it first, then confirm.",
        "IdentityUnresolved" => "Register or link the payor first.",
        "PossibleDuplicate" => $"Possible duplicate. Confirm it on its own and state why it is not (at least {MinimumDuplicateJustification} characters).",
        _ => "This collection cannot be confirmed now."
    };

    /// <summary>Flagging for correction: not on a reversed receipt and not twice in a row.</summary>
    public static string? FlagBlock(string disposition, string reviewState) =>
        disposition == "Reversed" ? "Reversed" : reviewState == NeedsCorrection ? "AlreadyFlagged" : null;

    /// <summary>Resolving closes an open flag only. It is not a verification: the receipt still has to be confirmed.</summary>
    public static string? ResolveBlock(string reviewState) => reviewState == NeedsCorrection ? null : "NotFlagged";

    /// <summary>
    /// The cash description for Collection Activity, only for Collector direct collections (null for every other source, which this does not govern).
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> LabelsAsync(IAppDbContext db, Guid tenant, IReadOnlyCollection<Guid> collectionIds, CancellationToken ct)
    {
        if (collectionIds.Count == 0) return new Dictionary<Guid, string>();
        var ids = collectionIds.ToArray();
        var direct = await ReviewableDirectCollections(db, tenant, DirectCollections(db, tenant)).Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.TotalAmount }).ToListAsync(ct);
        if (direct.Count == 0) return new Dictionary<Guid, string>();
        var directIds = direct.Select(x => x.Id).ToArray();
        var effects = await db.CollectionCorrections.AsNoTracking().Where(x => x.MunicipalityId == tenant && directIds.Contains(x.OriginalCollectionId))
            .GroupBy(x => x.OriginalCollectionId).Select(g => new { Id = g.Key, Effect = g.Sum(x => x.FinancialEffectAmount) }).ToDictionaryAsync(x => x.Id, x => x.Effect, ct);
        var latest = (await db.DirectCollectionReviewEvents.AsNoTracking().Where(e => e.MunicipalityId == tenant && directIds.Contains(e.CollectionId)).ToListAsync(ct))
            .GroupBy(e => e.CollectionId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).First().Outcome);
        return direct.ToDictionary(x => x.Id, x =>
        {
            if (x.TotalAmount + effects.GetValueOrDefault(x.Id) <= 0m) return LabelReversed;
            return latest.TryGetValue(x.Id, out var outcome) ? outcome switch
            {
                DirectCollectionReviewOutcome.Confirmed => LabelVerified,
                DirectCollectionReviewOutcome.NeedsCorrection => LabelNeedsCorrection,
                _ => LabelResolved
            } : LabelAwaiting;
        });
    }
}
