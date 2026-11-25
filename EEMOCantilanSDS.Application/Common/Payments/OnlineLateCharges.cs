using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>The month's surcharge and interest as Collection lines, the payer they are owed by, or why they cannot be taken now.</summary>
public sealed record OnlineLateChargeLines(IReadOnlyList<CollectionLineDraft> Lines, string? Occupant, string? Refusal)
{
    public decimal Total => Lines.Sum(l => l.Amount);
}

/// <summary>
/// A late month's surcharge and interest, paid online together with its rent (project owner, 2026-11-25). They are ordinary fine assessments
/// (<see cref="LatePenaltyAssessor"/>), so they are settled the way a collector settles them: one Collection line per charge, allocated to that
/// assessment, through the canonical coordinator. No OR is issued here; the office issues one afterwards, as for every online payment.
/// </summary>
public sealed class OnlineLateCharges(IAppDbContext db, IClock? clock = null)
{
    public const string Origin = "OnlineLateCharges";

    private DateOnly Today => clock?.PhilippineToday ?? PhilippineTime.Today;

    /// <summary>Deterministic client operation id for a transaction, so the webhook, the payor's return and any retry are one posting.</summary>
    public static Guid OperationIdFor(Guid transactionId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("stalltrack:online-late-charges:" + transactionId.ToString("N")))[..16]);

    /// <summary>What the month still owes in surcharge and interest on the space's present account: the figure its statement states.</summary>
    public async Task<decimal> OwedAsync(Guid tenant, Stall stall, int year, int month, CancellationToken ct) =>
        (await OwedFinesAsync(tenant, stall, year, month, ct)).Sum(f => f.Owed);

    public async Task<OnlineLateChargeLines> LinesAsync(Guid tenant, Stall stall, int year, int month, DateOnly date, CancellationToken ct)
    {
        var lines = new List<CollectionLineDraft>();
        string? occupant = null;
        try
        {
            foreach (var (id, _) in await OwedFinesAsync(tenant, stall, year, month, ct))
            {
                var facts = await new FineAssessmentCollectionSource(db).LoadAsync(tenant, id, date, tracked: true, ct);
                if (facts is null || !facts.Quote.CanSettle || facts.Quote.Remaining <= 0m) continue;
                occupant ??= facts.Quote.Occupant;
                lines.Add(new CollectionLineDraft(facts.Classification, facts.Policy, facts.Quote.Remaining, CollectionSourceKind.FineAssessment, id, null,
                    facts.Snapshot, [new CollectionAllocationDraft(CollectionSourceKind.FineAssessment, id, facts.Quote.Remaining, null, facts.Snapshot)]));
            }
        }
        catch (InvalidOperationException ex)
        {
            return new([], null, ex.Message);
        }
        return new(lines, occupant, null);
    }

    /// <summary>Posts the lines as one Collection. The caller's tracked changes (the transaction, the rent row) are saved in the same SaveChanges.</summary>
    public async Task PostAsync(OnlinePaymentTransaction tx, OnlineLateChargeLines charges, CancellationToken ct)
    {
        var paidAt = tx.PaidAt ?? DateTime.UtcNow;
        var businessDate = DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(paidAt));
        if (businessDate > Today) businessDate = Today;
        var intent = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            OperationType = "OnlineLateChargesCollection",
            TenantId = tx.MunicipalityId,
            TransactionId = tx.Id,
            tx.Reference,
            tx.GatewayReference,
            tx.PaymentId,
            Assessments = charges.Lines.Select(l => new { l.SourceId, Amount = l.Amount.ToString("0.00", CultureInfo.InvariantCulture) })
        });
        await new CanonicalCollectionPostingCoordinator(db, clock).PostAsync(tx.MunicipalityId, OperationIdFor(tx.Id), 1, intent, Origin,
            tx.PayorUserId.ToString("N"), "Online payor", "Payor", businessDate, "Online", charges.Lines, null, collectorId: null,
            payerName: charges.Occupant, ct: ct);
    }

    public Task<bool> PostedAsync(Guid tenant, Guid transactionId, CancellationToken ct)
    {
        var operation = OperationIdFor(transactionId);
        return db.PostingOperations.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenant && x.ClientOperationId == operation
            && x.Status == PostingOperationStatus.Succeeded, ct);
    }

    // The same reading as the statement's: the space's present account, surcharge and interest of that month, less what was collected on them.
    private async Task<List<(Guid Id, decimal Owed)>> OwedFinesAsync(Guid tenant, Stall stall, int year, int month, CancellationToken ct)
    {
        if (stall.Occupancies(Today).LastOrDefault()?.Contract.Id is not { } contract) return [];
        var start = new DateOnly(year, month, 1);
        var fines = await db.FineAssessments.AsNoTracking()
            .Where(a => a.MunicipalityId == tenant && a.ContractId == contract && a.RelatedPeriodStart == start
                && (a.PenaltyCode == FineAssessment.SurchargeCode || a.PenaltyCode == FineAssessment.InterestCode))
            .Select(a => new { a.Id, a.IncidentReference, Due = a.AdjustedAmount ?? a.AssessedAmount }).ToListAsync(ct);
        if (fines.Count == 0) return [];
        var settled = await new FineAssessmentCollectionSource(db).SettledAsync(tenant, fines.Select(f => f.Id).ToArray(), ct);
        return fines.OrderBy(f => f.IncidentReference, StringComparer.Ordinal)
            .Select(f => (f.Id, Owed: Math.Max(0m, f.Due - settled.GetValueOrDefault(f.Id)))).Where(f => f.Owed > 0m).ToList();
    }
}
