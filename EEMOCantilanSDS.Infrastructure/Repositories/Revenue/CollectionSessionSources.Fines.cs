using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

public sealed partial class CollectionSessionSources
{
    // This is a source-owned charge, not a new penalty definition or an inferred Business Payor debt.
    // The existing Composer is the single authorization boundary for Collector fine settlement.
    private async Task<FineSettlementFacts?> CollectibleFineAsync(
        Guid assessmentId, DateOnly businessDate, bool tracked, CancellationToken ct)
    {
        var permitted = await Composer.QuoteFineAsync(assessmentId, ct);
        if (!permitted.IsSuccess || permitted.Value?.CanSettle != true) return null;
        try
        {
            var facts = await new FineAssessmentCollectionSource(db)
                .LoadAsync(Tenant, assessmentId, businessDate, tracked, ct);
            return facts is { Quote.CanSettle: true, Quote.Instrument: RevenueInstrumentType.OfficialReceipt }
                ? facts : null;
        }
        catch (InvalidOperationException)
        {
            // Unavailable policy/evidence must not be presented as an amount the collector may collect.
            return null;
        }
    }

    private async Task AddAssessedFineChoicesAsync(
        List<CollectionSessionCapability> operations, CollectionSourceIdentity? identity,
        DateOnly businessDate, CancellationToken ct)
    {
        if (identity is not { Kind: SourceIdentityKind.Occupancy or SourceIdentityKind.SpaceAccount })
            return;
        var isContract = identity.Kind == SourceIdentityKind.Occupancy;
        var candidates = await db.FineAssessments.AsNoTracking()
            .Where(a => a.MunicipalityId == Tenant &&
                (isContract ? a.ContractId == identity.Id : a.ObligationAccountId == identity.Id))
            .OrderBy(a => a.RecordedAtUtc).ThenBy(a => a.Id).ToListAsync(ct);

        // Skip already-settled history with one correction-aware allocation read before
        // performing any per-fine policy and collector-authority checks.
        var settled = await new FineAssessmentCollectionSource(db)
            .SettledAsync(Tenant, candidates.Select(x => x.Id).ToArray(), ct);
        var choices = new List<CollectionSessionSourceChoice>();
        foreach (var fine in candidates.Where(x => x.AssessedAmount > settled.GetValueOrDefault(x.Id)))
        {
            var facts = await CollectibleFineAsync(fine.Id, businessDate, false, ct);
            if (facts is null || facts.Quote.Remaining <= 0m) continue;
            choices.Add(new(
                $"FINE|{fine.Id:N}", CollectionSessionItemKind.FineAssessment,
                RevenueClassificationCodes.PenaltiesAndFines, fine.PenaltyName,
                $"{fine.SourceName} · {fine.UnitReference} · Outstanding assessment",
                new(FineAssessmentId: fine.Id, FineVersion: facts.Quote.Version),
                RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.PreparedBalance,
                ServerAmount: facts.Quote.Remaining, MaximumAmount: facts.Quote.Remaining,
                RequiredInputs: []));
        }
        if (choices.Count == 0) return;
        operations.Add(new(CollectionSessionItemKind.FineAssessment, RevenueClassificationCodes.PenaltiesAndFines,
            "Fines / Penalties", true, true, null, null, false, [], true, choices,
            isContract ? CollectionFamily.Rent : CollectionFamily.Space));
    }

    // Contract/space-account identity is exact. A registered vendor only answers for a
    // separately office-verified linked occupancy, never for a same-name stall or payer.
    private async Task<bool> FineMatchesSessionAsync(
        CollectionSessionIntent session, FineAssessment fine, CancellationToken ct)
    {
        if (session.PayorId.HasValue || session.SourceIdentity is null) return false;
        if (fine.ContractId is { } contractId)
            return await SessionAnswersForOccupancyAsync(session, contractId, ct);
        return session.SourceIdentity is { Kind: SourceIdentityKind.SpaceAccount } identity
            && fine.ObligationAccountId == identity.Id;
    }

    private async Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteFineAsync(
        CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct)
    {
        (CollectionSessionItemQuote?, CollectionSessionProblem) Fail(string code, string reason) =>
            (null, new(item.ClientItemId, code, reason));
        if (item.Kind != CollectionSessionItemKind.FineAssessment ||
            item.Fine is not { AssessmentId: var id, Version: > 0 } || id == Guid.Empty)
            return Fail("InvalidIntent", "Choose an existing assessed fine.");
        var fine = await db.FineAssessments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.MunicipalityId == Tenant && a.Id == id, ct);
        if (fine is null || !await FineMatchesSessionAsync(session, fine, ct))
            return Fail("PayerMismatch", "The fine belongs to a different verified liable account.");

        var facts = await CollectibleFineAsync(id, session.BusinessDate, false, ct);
        if (facts is null) return Fail("SourceNotAvailable", "The fine is settled or not authorized for this collector.");
        if (facts.Quote.Version != item.Fine.Version)
            return Fail("BalanceChanged", "The fine balance changed. Refresh and review the remaining amount.");
        if (!ValidFinePayment(item.ConfirmedAmount, facts.Quote.Remaining))
            return Fail("InvalidAmount", "Enter a positive amount no greater than the outstanding fine, with at most two decimal places.");
        return (new(item.ClientItemId, item.Kind, RevenueClassificationCodes.PenaltiesAndFines,
            "Fines / Penalties", $"{facts.Assessment.PenaltyName} · {facts.Assessment.UnitReference}",
            RevenueInstrumentType.OfficialReceipt, item.ConfirmedAmount, facts.Quote.QuoteToken, Guid.Empty), null);
    }

    private static bool ValidFinePayment(decimal amount, decimal remaining) =>
        amount > 0m && amount <= remaining && decimal.Round(amount, 2) == amount;

    private async Task<CollectionSessionCollection> PostFineAsync(
        CollectionSessionIntent session, CollectionSessionItemIntent item, Guid operation, CancellationToken ct)
    {
        var id = item.Fine!.AssessmentId;
        var facts = await CollectibleFineAsync(id, session.BusinessDate, true, ct);
        if (facts is null || !await FineMatchesSessionAsync(session, facts.Assessment, ct)
            || facts.Quote.Version != item.Fine.Version || !ValidFinePayment(item.ConfirmedAmount, facts.Quote.Remaining))
            throw new CollectionSessionPostingException(item.ClientItemId,
                "The fine balance or collection authority changed. Review the checkout.");

        var line = new CollectionLineDraft(facts.Classification, facts.Policy,
            item.ConfirmedAmount, CollectionSourceKind.FineAssessment, id, null, facts.Snapshot,
            [new CollectionAllocationDraft(CollectionSourceKind.FineAssessment, id,
                item.ConfirmedAmount, null, facts.Snapshot)]);
        var actor = user.Username ?? "Collector";
        var version = facts.Assessment.Version;
        var normalized = JsonSerializer.Serialize(new {
            SchemaVersion = 1, session.ClientCollectionSessionId, session.SourceIdentity,
            session.BusinessDate, AssessmentId = id, Version = version, Amount = item.ConfirmedAmount
        });
        // This operates on the SAME database context/serializable transaction as the checkout;
        // a failed sibling item or concurrent settlement rolls back the SRC and allocation.
        var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
            Tenant, operation, 1, normalized, "MobileCollectionSessionFine",
            user.UserId!.Value.ToString("D"), actor, "Collector",
            session.BusinessDate, actor, [line], collectorId: user.CollectorId,
            payerName: facts.Assessment.Occupant,
            sourceProjections: [new Action<DateTime>(_ => facts.Assessment.ClaimSettlement(version))],
            ct: ct);
        return new(collection.Id, collection.ReferenceCode, RevenueInstrumentType.OfficialReceipt,
            collection.TotalAmount, [item.ClientItemId], "Posted");
    }
}
