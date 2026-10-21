using System.Text.Json;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Payments;

public sealed partial class NpmRentMonthWorkflow
{
    /// <summary>Explicit Head credit decision; never inferred from a payment or an unreviewed spreadsheet.</summary>
    public async Task<Result<long>> ReviseCreditsAsync(Guid stallId, DateOnly period, decimal approvedCredits,
        string evidenceJson, long expectedVersion, string reason, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role is not ("SuperAdmin" or "Admin")) return Result<long>.Forbidden();
        await using var transaction = db.HasActiveTransaction ? null : await db.BeginSerializableTransactionAsync(ct);
        try
        {
            var loaded = await Load(stallId, period, ct);
            db.AuditLogs.Add(loaded.Month.ReviseCredits(approvedCredits, evidenceJson, expectedVersion, user.UserId!.Value,
                user.Username!, user.Role!, clock.UtcNow, reason));
            await db.SaveChangesAsync(ct);
            if (transaction is not null) { await transaction.CommitAsync(ct); await InvalidateAfterCommitAsync(ct); }
            return Result<long>.Success(loaded.Month.SettlementVersion);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbUpdateException or System.Data.Common.DbException)
        {
            db.ChangeTracker.Clear();
            return Result<long>.Failure("Credit revision was not confirmed; refresh the month evidence before retrying.", ResultStatus.Conflict);
        }
    }

    /// <summary>Called INSIDE the existing correction transaction before saving its financial event.</summary>
    public async Task TouchCorrectionMonthsAsync(IReadOnlyCollection<CollectionAllocation> allocations,
        string operationEvidence, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            if (allocations.Any(x => x.SourceKind == CollectionSourceKind.NpmRentMonth))
                throw new InvalidOperationException("NPM month correction routing is unavailable.");
            return;
        }
        if (!await Authorized(ct) || !db.HasActiveTransaction)
            throw new InvalidOperationException("An authorized coordinated correction transaction is required.");
        if (allocations.Any(x => x.MunicipalityId != Tenant))
            throw new InvalidOperationException("Correction evidence belongs to another municipality.");
        var monthIds = allocations.Where(x => x.SourceKind == CollectionSourceKind.NpmRentMonth).Select(x => x.SourceId).Distinct().ToArray();
        var dayIds = allocations.Where(x => x.SourceKind == CollectionSourceKind.DailyCollection && x.SourcePart == CollectionSourcePart.DailyFee)
            .Select(x => x.SourceId).Distinct().ToArray();
        var days = await db.DailyCollections.AsNoTracking().Where(x => x.MunicipalityId == Tenant && dayIds.Contains(x.Id)).ToListAsync(ct);
        var stallIds = days.Select(x => x.StallId).Distinct().ToArray();
        var months = await db.NpmRentMonths.Where(x => x.MunicipalityId == Tenant && (monthIds.Contains(x.Id) || stallIds.Contains(x.StallId)))
            .OrderBy(x => x.Id).ToListAsync(ct);
        foreach (var month in months)
        {
            var direct = monthIds.Contains(month.Id);
            var matching = days.Where(x => x.StallId == month.StallId && x.CollectionDate.Year == month.PeriodStart.Year
                && x.CollectionDate.Month == month.PeriodStart.Month).Select(x => x.Id).ToArray();
            if (!direct && matching.Length == 0) continue;
            var manifest = ParseOpening(month.ReconciliationEvidence);
            if (!direct && !matching.All(x => manifest.CanonicalDailyIds.Contains(x)))
                throw new InvalidOperationException("The historical allocation is not part of reviewed month opening evidence.");
            db.AuditLogs.Add(month.RecordSettlementChange(month.SettlementVersion, user.UserId!.Value, user.Username!, user.Role!,
                clock.UtcNow, operationEvidence));
        }
        if (monthIds.Any(id => months.All(x => x.Id != id)))
            throw new InvalidOperationException("The month correction source is unavailable in this tenant.");
    }

    /// <summary>Head-only full reversal; no new Collection, SRC, receipt consumption or remittance.</summary>
    public async Task<Result<NpmRentMonthPaymentOutcome>> ReverseAsync(Guid collectionId, Guid clientOperationId,
        string reason, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role is not ("SuperAdmin" or "Admin"))
            return Result<NpmRentMonthPaymentOutcome>.Forbidden();
        if (collectionId == Guid.Empty || clientOperationId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result<NpmRentMonthPaymentOutcome>.Failure("A collection, stable correction identity and reason are required.", ResultStatus.Invalid);
        const string correctionOrigin = "NpmRentMonthCorrection";
        var actor = user.UserId!.Value.ToString("N");
        var intent = JsonSerializer.Serialize(new { version = 1, Tenant, collectionId, reason = reason.Trim(), actor }, Json);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(1, intent, correctionOrigin, actor);
        var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == clientOperationId, ct);
        if (prior is not null)
        {
            if (prior.Origin != correctionOrigin || prior.IntentFingerprint != fingerprint || prior.Status != PostingOperationStatus.Succeeded
                || prior.CollectionId != collectionId)
                return Result<NpmRentMonthPaymentOutcome>.Failure("IDEMPOTENCY CONFLICT: correction intent changed.", ResultStatus.Conflict);
            var original = await db.Collections.AsNoTracking().SingleAsync(x => x.MunicipalityId == Tenant && x.Id == collectionId, ct);
            return Result<NpmRentMonthPaymentOutcome>.Success(new(original.Id, original.ReferenceCode, 0, true));
        }
        await using var transaction = db.HasActiveTransaction ? null : await db.BeginSerializableTransactionAsync(ct);
        var collection = await db.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == collectionId, ct);
        if (collection is null) return Result<NpmRentMonthPaymentOutcome>.NotFound();
        var allocations = collection.Lines.SelectMany(x => x.Allocations).ToArray();
        if (allocations.Length == 0 || allocations.Any(x => x.SourceKind != CollectionSourceKind.NpmRentMonth)
            || collection.Lines.Any(x => x.Allocations.Sum(a => a.Amount) != x.Amount))
            return Result<NpmRentMonthPaymentOutcome>.Failure("Only a fully attributed NPM month collection can be reversed here.", ResultStatus.Conflict);
        if (await db.CollectionRemittanceCoverages.AnyAsync(x => x.MunicipalityId == Tenant && x.CollectionId == collectionId && x.IsActive, ct))
            return Result<NpmRentMonthPaymentOutcome>.Failure("Remitted cash requires the established remittance reconciliation process.", ResultStatus.Conflict);
        if (await db.CollectionCorrections.AnyAsync(x => x.MunicipalityId == Tenant && x.OriginalCollectionId == collectionId, ct))
            return Result<NpmRentMonthPaymentOutcome>.Failure("This collection already has a correction.", ResultStatus.Conflict);
        try
        {
            await TouchCorrectionMonthsAsync(allocations, "Canonical month reversal " + clientOperationId, ct);
            var utc = clock.UtcNow;
            db.CollectionCorrections.Add(CollectionCorrection.Record(Tenant, collectionId, null, null, null, CollectionCorrectionType.Reversal,
                clock.PhilippineToday, utc, -collection.TotalAmount, reason.Trim(), actor, user.Username!,
                collection.Lines.Select(x => new CollectionCorrectionLineDraft(x.Id, -x.Amount,
                    x.Allocations.Select(a => new CollectionCorrectionAllocationDraft(a.Id, -a.Amount)).ToArray()))));
            db.PostingOperations.Add(PostingOperation.Record(Tenant, clientOperationId, 1, intent, correctionOrigin, actor,
                PostingOperationStatus.Succeeded, null, null, collectionId, null, utc));
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
                await InvalidateAfterCommitAsync(ct);
            }
            return Result<NpmRentMonthPaymentOutcome>.Success(new(collection.Id, collection.ReferenceCode, 0, false));
        }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException
            or InvalidOperationException { InnerException: DbUpdateException or System.Data.Common.DbException })
        {
            db.ChangeTracker.Clear();
            return Result<NpmRentMonthPaymentOutcome>.Failure("RECONCILIATION_REQUIRED: retry the same correction identity after refreshing the month.", ResultStatus.Conflict);
        }
    }
}
