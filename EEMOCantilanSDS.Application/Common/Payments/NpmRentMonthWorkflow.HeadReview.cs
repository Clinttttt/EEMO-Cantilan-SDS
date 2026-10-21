using System.Text.Json;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Application.Common.Revenue;

namespace EEMOCantilanSDS.Application.Common.Payments;

public sealed partial class NpmRentMonthWorkflow
{
    public async Task<Result<NpmRentMonthHeadReview>> ReviewHeadAsync(Guid stallId, DateOnly period, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role is not ("SuperAdmin" or "Admin")) return Result<NpmRentMonthHeadReview>.Forbidden();
        var quote = await QuoteAsync(stallId, period, clock.PhilippineToday, NpmRentPaymentScope.WholeMonth, ct);
        if (!quote.IsSuccess) return Result<NpmRentMonthHeadReview>.Failure(quote.Error!, quote.Status);
        var cash = await ReadCashEventsAsync(stallId, period, ct);
        var ids = cash.Select(x => x.CollectionId).ToArray();
        var covered = await db.CollectionRemittanceCoverages.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.CollectionId) && x.IsActive)
            .Select(x => x.CollectionId).Distinct().ToListAsync(ct);
        return Result<NpmRentMonthHeadReview>.Success(new(quote.Value!, cash, covered));
    }

    public async Task<Result<NpmRentMonthPaymentOutcome>> ReverseReviewedAsync(NpmRentMonthReverseRequest request, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role is not ("SuperAdmin" or "Admin")) return Result<NpmRentMonthPaymentOutcome>.Forbidden();
        var loaded = await Load(request.StallId, request.PeriodStart, ct);
        var allocations = await db.CollectionAllocations.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.SourceKind == CollectionSourceKind.NpmRentMonth
            && x.SourceId == loaded.Month.Id).Join(db.CollectionLines, a => a.CollectionLineId, l => l.Id, (a, l) => new { a, l.CollectionId })
            .Where(x => x.CollectionId == request.CollectionId).ToListAsync(ct);
        if (allocations.Count == 0) return Result<NpmRentMonthPaymentOutcome>.NotFound();
        var replay = await db.PostingOperations.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
        if (!replay && loaded.Month.SettlementVersion != request.ExpectedVersion)
            return Result<NpmRentMonthPaymentOutcome>.ValidationFailure(new() { [nameof(request.ExpectedVersion)] = ["The month version changed. No correction was attempted. Review the refreshed month."] });
        return await ReverseAsync(request.CollectionId, request.ClientOperationId, request.Reason, ct);
    }

    /// <summary>Current-day, same-source entry correction; never authorizes additional cash or a historical refund.</summary>
    public async Task<Result<NpmRentMonthPaymentOutcome>> ReplaceReviewedAsync(NpmRentMonthReplacementRequest request, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role is not ("SuperAdmin" or "Admin")) return Result<NpmRentMonthPaymentOutcome>.Forbidden();
        if (request.ClientOperationId == Guid.Empty || request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 300
            || string.IsNullOrWhiteSpace(request.AuthorityReference) || request.AuthorityReference.Length > 150)
            return Result<NpmRentMonthPaymentOutcome>.Failure("Supply a stable operation, verified amount, review reference and reason.", ResultStatus.Invalid);
        const string origin = "NpmRentMonthReplacement";
        var actor = user.UserId!.Value.ToString("N");
        var intent = JsonSerializer.Serialize(new { Tenant, actor, request }, Json);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(1, intent, origin, actor);
        var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.Origin != origin || prior.IntentFingerprint != fingerprint || prior.Status != PostingOperationStatus.Succeeded)
                return Result<NpmRentMonthPaymentOutcome>.Failure("IDEMPOTENCY CONFLICT: replacement intent changed.", ResultStatus.Conflict);
            var saved = JsonSerializer.Deserialize<NpmRentMonthPaymentOutcome>(prior.OutcomeDetails!, Json)!;
            return Result<NpmRentMonthPaymentOutcome>.Success(saved with { Existing = true });
        }
        // A failure must roll back reversal AND replacement together; never return into an unknown outer transaction.
        if (db.ChangeTracker.Context.Database.CurrentTransaction is not null)
            return Result<NpmRentMonthPaymentOutcome>.Failure("Replacement requires its own controlled transaction.", ResultStatus.Conflict);
        await using var transaction = await db.BeginSerializableTransactionAsync(ct);
        try
        {
            var original = await db.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations)
                .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == request.CollectionId, ct);
            var allocations = original?.Lines.SelectMany(x => x.Allocations).ToArray() ?? [];
            if (original is null || allocations.Length != 1 || allocations[0].SourceKind != CollectionSourceKind.NpmRentMonth)
                throw new InvalidOperationException("Only one verified month-source collection can be replaced here.");
            if (original.BusinessDate != clock.PhilippineToday || request.Amount > original.TotalAmount)
                throw new InvalidOperationException("Historical corrections or additional cash require separate office reconciliation; no refund or new cash is inferred.");
            if (original.CollectorId is not null)
                throw new InvalidOperationException("Collector-owned cash requires its custody-preserving correction workflow; an office replacement cannot reassign it.");
            var source = await db.NpmRentMonths.SingleAsync(x => x.MunicipalityId == Tenant && x.Id == allocations[0].SourceId, ct);
            var loaded = await Load(source.StallId, source.PeriodStart, ct);
            if (source.SettlementVersion != request.ExpectedVersion)
                return Result<NpmRentMonthPaymentOutcome>.ValidationFailure(new() { [nameof(request.ExpectedVersion)] = ["The month version changed. No correction was attempted. Review the refreshed month."] });
            var scope = request.Amount == loaded.Position.Outstanding + original.TotalAmount ? NpmRentPaymentScope.WholeMonth : NpmRentPaymentScope.PartPayment;
            using var policy = JsonDocument.Parse(source.RateEvidenceJson);
            if (scope == NpmRentPaymentScope.PartPayment && (!partPayments.Enabled || !Approved(policy, "partPaymentsApproved")))
                throw new InvalidOperationException("The corrected amount needs approved partial-payment authority. Review it with the office.");
            var reversed = await ReverseAsync(original.Id, CollectionSessionWorkflow.ChildOperationId(Tenant, request.ClientOperationId, original.Id),
                request.AuthorityReference.Trim() + ": " + request.Reason.Trim(), ct);
            if (!reversed.IsSuccess) throw new InvalidOperationException(reversed.Error);
            var quote = await QuoteAsync(source.StallId, source.PeriodStart, clock.PhilippineToday, scope, ct);
            if (!quote.IsSuccess) throw new InvalidOperationException(quote.Error);
            var q = quote.Value!;
            var replacement = await PostAsync(new(CollectionSessionWorkflow.ChildOperationId(Tenant, request.ClientOperationId, source.Id), source.StallId,
                source.PeriodStart, q.BusinessDate, scope, request.Amount, q.Token), ct);
            if (!replacement.IsSuccess) throw new InvalidOperationException(replacement.Error);
            var correction = await db.CollectionCorrections.SingleAsync(x => x.MunicipalityId == Tenant && x.OriginalCollectionId == original.Id, ct);
            correction.CompleteReplacement(replacement.Value!.CollectionId);
            db.PostingOperations.Add(PostingOperation.Record(Tenant, request.ClientOperationId, 1, intent, origin, actor,
                PostingOperationStatus.Succeeded, null, JsonSerializer.Serialize(replacement.Value, Json), replacement.Value.CollectionId, null, clock.UtcNow));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await InvalidateAfterCommitAsync(ct);
            return replacement;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbUpdateException or System.Data.Common.DbException)
        {
            db.ChangeTracker.Clear();
            return Result<NpmRentMonthPaymentOutcome>.Failure("Replacement is not confirmed: " + ex.Message + " Retry the same operation after review.", ResultStatus.Conflict);
        }
    }
    public async Task<Result<NpmRentMonthCreditOutcome>> ApplyReviewedCreditAsync(NpmRentMonthCreditRequest request, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role is not ("SuperAdmin" or "Admin")) return Result<NpmRentMonthCreditOutcome>.Forbidden();
        if (request.ClientOperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.AuthorityReference)
            || request.AuthorityReference.Trim().Length > 200 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Result<NpmRentMonthCreditOutcome>.Failure("A stable operation, approved authority reference and review reason are required.", ResultStatus.Invalid);
        var fingerprint = Hash(new { Tenant, actor = user.UserId, request });
        var existing = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == request.ClientOperationId, ct);
        if (existing is not null)
        {
            if (existing.Action != "ReviseNpmRentMonthCredits")
                return Result<NpmRentMonthCreditOutcome>.Failure("IDEMPOTENCY CONFLICT: this identity belongs to another audit event.", ResultStatus.Conflict);
            try
            {
                using var values = JsonDocument.Parse(existing.NewValues ?? "");
                using var evidence = JsonDocument.Parse(values.RootElement.GetProperty("CreditEvidenceJson").GetString()!);
                if (!evidence.RootElement.TryGetProperty("requestFingerprint", out var original)
                    || original.GetString() != fingerprint)
                    return Result<NpmRentMonthCreditOutcome>.Failure("IDEMPOTENCY CONFLICT: the reviewed credit intent changed.", ResultStatus.Conflict);
                return Result<NpmRentMonthCreditOutcome>.Success(new(values.RootElement.GetProperty("SettlementVersion").GetInt64(), true));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            { return Result<NpmRentMonthCreditOutcome>.Failure("Existing credit evidence requires reconciliation before replay.", ResultStatus.Conflict); }
        }
        await using var transaction = db.HasActiveTransaction ? null : await db.BeginSerializableTransactionAsync(ct);
        try
        {
            var loaded = await Load(request.StallId, request.PeriodStart, ct);
            if (loaded.Month.SettlementVersion != request.ExpectedVersion)
                return Result<NpmRentMonthCreditOutcome>.ValidationFailure(new() { [nameof(request.ExpectedVersion)] = ["The month version changed. No credit was revised. Review the refreshed month."] });
            if (request.ApprovedCredits < 0 || request.ApprovedCredits > loaded.Month.AssessedAmount || decimal.Round(request.ApprovedCredits, 2) != request.ApprovedCredits)
                return Result<NpmRentMonthCreditOutcome>.Failure("Credits must be a reviewed amount between zero and the approved assessment, with at most two decimal places.", ResultStatus.Invalid);
            var evidence = JsonSerializer.Serialize(new { schemaVersion = 1, source = "ExplicitHeadDecision",
                reference = request.AuthorityReference.Trim(), requestFingerprint = fingerprint, request.ClientOperationId }, Json);
            db.AuditLogs.Add(loaded.Month.ReviseCredits(request.ApprovedCredits, evidence, request.ExpectedVersion,
                user.UserId!.Value, user.Username!, user.Role!, clock.UtcNow, request.Reason, request.ClientOperationId));
            await db.SaveChangesAsync(ct);
            if (transaction is not null) { await transaction.CommitAsync(ct); await InvalidateAfterCommitAsync(ct); }
            return Result<NpmRentMonthCreditOutcome>.Success(new(loaded.Month.SettlementVersion, false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbUpdateException or System.Data.Common.DbException)
        {
            db.ChangeTracker.Clear();
            return Result<NpmRentMonthCreditOutcome>.Failure("Credit revision is not confirmed. Retry the same operation or refresh/reconcile the reviewed evidence; no cash refund is authorized.", ResultStatus.Conflict);
        }
    }
}
