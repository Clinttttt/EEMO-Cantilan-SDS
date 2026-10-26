using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>What an online checkout for a Canonical monthly-rent row may charge, or why it may not.</summary>
public sealed record OnlineRentQuote(bool Payable, decimal Amount, string? Reason);

/// <summary>Result of posting one captured online payment. <see cref="Rejected"/> means nothing was posted.</summary>
public sealed record OnlineRentPostOutcome(bool Posted, bool AlreadyPosted, string? RejectionReason)
{
    public static OnlineRentPostOutcome Rejected(string reason) => new(false, false, reason);
}

/// <summary>
/// ST-17. Posts ONE canonical Collection (SRC) for a settled online monthly Stall Rent payment against a row whose settlement
/// authority is already Canonical. It reuses the same pieces as the Mobile/Office rent writer
/// (<see cref="MonthlyRentCollectionSourceAdapter"/> facts, a PaymentRecord allocation, <c>ApplyCanonicalRentProjection</c>,
/// <see cref="CanonicalCollectionPostingCoordinator"/>) and, like them, carries no physical OR serial: the OR stays a separate
/// staff step.
///
/// <para>
/// Idempotency: the client operation id is derived from the transaction id, so the webhook, the payor-return confirm and any
/// retry all compute the same id and the unique (municipality, clientOperationId) posting operation makes them one posting.
/// The transaction's Paid state is saved in the SAME SaveChanges as the Collection, so there is no window with money
/// captured but unposted. The SRC is reachable from the transaction through that operation id; no schema column is needed.
/// </para>
///
/// <para>
/// Actor: the paying portal user (ActorId = PayorUserId, role "Payor"), so the collection is attributable to the payor's own
/// authenticated checkout and carries no collector. The business Payor on the collection is only the contract's explicit link.
/// </para>
/// </summary>
public sealed class OnlineRentCanonicalPoster(IAppDbContext db, IClock? clock = null)
{
    public const string Origin = "OnlineRent";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Deterministic client operation id for a transaction (never random).</summary>
    public static Guid OperationIdFor(Guid transactionId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("stalltrack:online-rent:" + transactionId.ToString("N")))[..16]);

    private DateOnly Today => clock?.PhilippineToday ?? PhilippineTime.Today;

    /// <summary>Pre-checkout: the amount a Canonical row can take online right now (its rent outstanding), or why not.</summary>
    public async Task<OnlineRentQuote> QuoteAsync(PaymentRecord record, CancellationToken ct)
    {
        try
        {
            var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadAsync(record.MunicipalityId, record.StallId,
                record.BillingYear, record.BillingMonth, Today, tracked: false, materializeMissingSource: false, actor: "Online", ct: ct);
            var reason = Validate(facts, record, null);
            return reason is null ? new(true, facts!.Quote.OutstandingAmount, null) : new(false, 0m, reason);
        }
        catch (InvalidOperationException ex)
        {
            return new(false, 0m, ex.Message);
        }
    }

    public async Task<OnlineRentPostOutcome> PostAsync(OnlinePaymentTransaction tx, PaymentRecord record, CancellationToken ct)
    {
        var tenantId = tx.MunicipalityId;
        var operationId = OperationIdFor(tx.Id);
        if (await PriorSucceededAsync(tenantId, operationId, ct))
            return new(false, true, null);

        try
        {
            var paidAt = tx.PaidAt ?? DateTime.UtcNow;
            var businessDate = DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(paidAt));
            if (businessDate > Today) businessDate = Today;
            var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadAsync(tenantId, record.StallId,
                record.BillingYear, record.BillingMonth, businessDate, tracked: true, materializeMissingSource: false,
                actor: "Online", ct: ct);
            if (Validate(facts, record, tx.Amount) is { } reason)
                return OnlineRentPostOutcome.Rejected(reason);

            var amount = tx.Amount;
            var normalized = JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                OperationType = "OnlineRentCollection",
                TenantId = tenantId,
                TransactionId = tx.Id,
                tx.Reference,
                tx.GatewayReference,
                tx.PaymentId,
                StallId = record.StallId,
                record.BillingYear,
                record.BillingMonth,
                facts!.Quote.SourceVersion,
                ReceivedAmount = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            }, Json);

            const string lineSnapshot = "{\"schemaVersion\":1,\"lineKind\":\"PermanentStallRent\"}";
            var line = new CollectionLineDraft(facts.Classification, facts.Policy, amount, null, null, null, lineSnapshot,
                [new CollectionAllocationDraft(CollectionSourceKind.PaymentRecord, record.Id, amount, null, facts.SnapshotJson)]);
            var cumulative = facts.Quote.CumulativeSettledEvidence + amount;
            var actorId = tx.PayorUserId.ToString("N");
            var projection = new Action<DateTime>(now => facts.Record!.ApplyCanonicalRentProjection(cumulative, now, "Online"));

            await new CanonicalCollectionPostingCoordinator(db, clock).PostAsync(
                tenantId, operationId, IntentVersion, normalized, Origin, actorId, "Online payor", "Payor",
                businessDate, "Online", [line], null, collectorId: null, payorId: facts.Quote.PayorId,
                payerName: facts.Quote.PayerNameSnapshot, sourceProjections: [projection], ct: ct);
            return new(true, false, null);
        }
        catch (DbUpdateException)
        {
            // Lost a race (unique posting operation, or the transaction's concurrency token): the winner owns the money event.
            db.ChangeTracker.Clear();
            if (await PriorSucceededAsync(tenantId, operationId, ct))
                return new(false, true, null);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            db.ChangeTracker.Clear();
            return OnlineRentPostOutcome.Rejected("Rent source evidence requires office reconciliation: " + ex.Message);
        }
    }

    private Task<bool> PriorSucceededAsync(Guid tenantId, Guid operationId, CancellationToken ct) =>
        db.PostingOperations.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenantId
            && x.ClientOperationId == operationId && x.Status == PostingOperationStatus.Succeeded, ct);

    // amount null = quote only. Any failure keeps the capture for reconciliation; nothing is posted and no balance is cleared.
    private static string? Validate(MonthlyRentCollectionSourceAdapter.RentSourceFacts? facts, PaymentRecord record, decimal? amount)
    {
        if (facts?.Record is not { } source || source.Id != record.Id)
            return "No canonical rent source exists for this stall and month.";
        if (source.SettlementAuthorityState != SettlementAuthority.Canonical)
            return "The rent source is not under canonical settlement authority.";
        if (facts.Quote.RequiresLegacyReconciliation)
            return "The rent row has mixed legacy components and requires reconciliation first.";
        if (!facts.Quote.CanPostCanonical)
            return "The rental period has no collectible rent balance (already settled by another channel, or not collectible).";
        if (amount is { } paid && paid != facts.Quote.OutstandingAmount)
            return paid > facts.Quote.OutstandingAmount
                ? "The captured amount exceeds the rent outstanding now; another channel settled part of it after checkout."
                : "The rent outstanding changed after checkout; the captured amount no longer equals it.";
        return null;
    }
}
