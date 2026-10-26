using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The durable late-payload adapter for monthly rent (the counterpart of the Water one). Once a monthly PaymentRecord has left Legacy
/// authority, an older cumulative status payload (a Mobile offline replay, or a stale Web screen) can no longer change it. It is not
/// dropped: what was submitted is kept as one ReconciliationRequired <see cref="PostingOperation"/> bound to its ClientOperationId, so the
/// office sees the cash a collector may have taken, and no Collection, SRC, balance or amount is created or changed.
/// </summary>
/// <remarks>
/// Idempotent: the same ClientOperationId and the same payload returns the preserved outcome again; the same id with a different payload
/// is an idempotency conflict. When the caller supplies no id (a Web request) a deterministic one is derived from the source and the
/// payload, so a double click preserves one record. The submitted OR number is evidence in the intent only; it neither registers nor
/// consumes any accountable document.
/// </remarks>
public sealed class LegacyRentPayloadReconciliation(IAppDbContext db, IClock? clock = null)
{
    public const string OutcomeCode = "OLD_MOBILE_PAYLOAD_AFTER_CUTOVER";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTime UtcNow => clock?.UtcNow ?? DateTime.UtcNow;

    public async Task<Result<string>> PreserveAsync(
        Guid tenantId, PaymentRecord source, RecordPaymentCommand request, string actorId, bool fromWeb, CancellationToken ct)
    {
        var origin = fromWeb ? "WebLegacyRentAfterCutover" : "MobileLegacyRentAfterCutover";
        var operationId = request.ClientOperationId is { } supplied && supplied != Guid.Empty
            ? supplied
            : DerivedOperationId(tenantId, source.Id, request, actorId);
        var normalized = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            OperationType = "LegacyCumulativeRentPayloadAfterCutover",
            TenantId = tenantId,
            ActorId = actorId,
            ClientOperationId = operationId,
            PaymentRecordId = source.Id,
            source.StallId,
            source.BillingYear,
            source.BillingMonth,
            SubmittedStatus = request.Status.ToString(),
            SubmittedPartialAmount = request.PartialAmount,
            SubmittedDocumentNumber = request.ORNumber?.Trim(),
            DocumentField = "LegacyRentORNumber",
            CurrentSettlementAuthority = source.SettlementAuthorityState.ToString(),
            CurrentSourceVersion = source.SettlementVersion,
            CurrentAssessment = source.BaseRentalAmount
        }, Json);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, origin, actorId);

        var prior = await FindAsync(tenantId, operationId, ct);
        if (prior is not null) return Resolve(prior, origin, actorId, fingerprint);

        var details = "An older cumulative monthly-rent payload arrived after this month moved off Legacy settlement. No Collection, SRC or balance was created. "
            + $"Preserved status {request.Status}, amount evidence {request.PartialAmount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "not supplied"}, "
            + $"and legacy document field '{request.ORNumber?.Trim() ?? "not supplied"}'. Office reconciliation is required.";
        db.PostingOperations.Add(PostingOperation.Record(tenantId, operationId, IntentVersion, normalized, origin, actorId,
            PostingOperationStatus.ReconciliationRequired, OutcomeCode, details, null, null, UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindAsync(tenantId, operationId, ct);
            return winner is null
                ? Result<string>.Failure("The old monthly-rent payload conflicts with a recorded ClientOperationId; reconcile its original evidence.", ResultStatus.Conflict)
                : Resolve(winner, origin, actorId, fingerprint);
        }
        return Result<string>.Success(details);
    }

    private static Result<string> Resolve(PostingOperation prior, string origin, string actorId, string fingerprint) =>
        prior.Origin != origin || prior.ActorId != actorId || prior.IntentFingerprint != fingerprint
            ? Result<string>.Failure("IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to another posting intent.", ResultStatus.Conflict)
            : Result<string>.Success(prior.OutcomeDetails ?? "The old monthly-rent payload is preserved for reconciliation.");

    private Task<PostingOperation?> FindAsync(Guid tenantId, Guid operationId, CancellationToken ct) =>
        db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.ClientOperationId == operationId, ct);

    private static Guid DerivedOperationId(Guid tenantId, Guid sourceId, RecordPaymentCommand r, string actorId)
    {
        var material = string.Join('|', "stalltrack:legacy-rent-late", tenantId.ToString("N"), sourceId.ToString("N"), actorId,
            r.Status, r.PartialAmount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), r.ORNumber?.Trim());
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(material))[..16]);
    }
}
