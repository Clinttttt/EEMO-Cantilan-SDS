using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// An immutable posted money-received event. This Phase 2A entity is a dormant ledger foundation;
/// current operational money sources remain authoritative until a later adapter is approved.
/// </summary>
public sealed class Collection : BaseEntity, IMunicipalityOwned
{
    internal const decimal MaximumMoneyAmount = 9_999_999_999_999_999.99m;

    private readonly List<CollectionLine> _lines = [];

    public Guid MunicipalityId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }

    public string ActorId { get; private set; } = string.Empty;
    public string ActorName { get; private set; } = string.Empty;
    public string ActorRole { get; private set; } = string.Empty;

    public Guid? CollectorId { get; private set; }
    public Guid? PayorId { get; private set; }
    public Guid? PayorUserId { get; private set; }
    public string? PayerName { get; private set; }

    public decimal TotalAmount { get; private set; }
    public Guid? ClientOperationId { get; private set; }

    /// <summary>The year part of the reference code: the Philippine calendar year the collection was recorded. Presentation context only.</summary>
    public int ReferenceYear { get; private set; }

    /// <summary>
    /// The permanent global sequence number of this collection, allocated by the database from one monotonic sequence when the
    /// collection is saved (IA-062). It is unique across every tenant, never reset (not by year, remittance or collector), never
    /// reused and never edited. A gap left by a failed transaction is acceptable: this is an application reference, not an
    /// accountable-form series. Zero only before the first save.
    /// </summary>
    public long ReferenceNumber { get; private set; }

    /// <summary>
    /// The StallTrack Reference Code (SRC), for example SRC-2026-000127: a database-computed function of year and number, so it can
    /// never drift from them. SRC is NOT an Official Receipt, a Cash Ticket or any government receipt number. Empty before the first
    /// save.
    /// </summary>
    public string ReferenceCode { get; private set; } = string.Empty;

    public IReadOnlyCollection<CollectionLine> Lines => _lines.AsReadOnly();

    private Collection() { }

    /// <summary>
    /// Constructs the complete immutable aggregate. The tenant is derived from the tenant-scoped
    /// classification references, and the total is computed from its lines rather than supplied.
    /// </summary>
    public static Collection Post(
        DateOnly businessDate,
        DateTime recordedAtUtc,
        string actorId,
        string actorName,
        string actorRole,
        IEnumerable<CollectionLineDraft> lines,
        Guid? collectorId = null,
        Guid? payorUserId = null,
        string? payerName = null,
        Guid? clientOperationId = null,
        Guid? payorId = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var drafts = lines.ToList();
        if (drafts.Count == 0)
            throw new ArgumentException("A collection must contain at least one line.", nameof(lines));
        if (drafts.Any(x => x is null))
            throw new ArgumentException("Collection lines cannot be null.", nameof(lines));

        ValidateRequiredSnapshot(actorId, nameof(actorId), 100);
        ValidateRequiredSnapshot(actorName, nameof(actorName), 150);
        ValidateRequiredSnapshot(actorRole, nameof(actorRole), 50);
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Recorded time must be UTC.", nameof(recordedAtUtc));
        ValidateOptionalId(collectorId, nameof(collectorId));
        ValidateOptionalId(payorUserId, nameof(payorUserId));
        ValidateOptionalId(clientOperationId, nameof(clientOperationId));
        ValidateOptionalId(payorId, nameof(payorId));
        if (payerName?.Length > 200)
            throw new ArgumentException("Payer name must not exceed 200 characters.", nameof(payerName));
        if (payorId.HasValue && string.IsNullOrWhiteSpace(payerName))
            throw new ArgumentException("A linked Payor requires frozen payer-name evidence.", nameof(payerName));

        var municipalityId = drafts[0].Classification?.MunicipalityId
            ?? throw new ArgumentException("Every line requires a classification.", nameof(lines));
        decimal total = 0;
        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            BusinessDate = businessDate,
            RecordedAtUtc = recordedAtUtc,
            ActorId = actorId.Trim(),
            ActorName = actorName.Trim(),
            ActorRole = actorRole.Trim(),
            CollectorId = collectorId,
            PayorId = payorId,
            PayorUserId = payorUserId,
            PayerName = string.IsNullOrWhiteSpace(payerName) ? null : payerName.Trim(),
            ClientOperationId = clientOperationId,
            ReferenceYear = PhilippineTime.ToPhilippineTime(recordedAtUtc).Year
        };

        foreach (var draft in drafts)
        {
            if (draft.Classification is null || draft.Policy is null)
                throw new ArgumentException("Every line requires a classification and policy.", nameof(lines));
            if (draft.Classification.MunicipalityId != municipalityId
                || draft.Policy.MunicipalityId != municipalityId)
                throw new ArgumentException("All collection lines and policies must belong to one municipality.", nameof(lines));
            if (draft.Policy.RevenueClassificationId != draft.Classification.Id)
                throw new ArgumentException("The policy must belong to the line's revenue classification.", nameof(lines));
            if (draft.Policy.EffectiveDate > businessDate)
                throw new ArgumentException("A collection cannot use a policy that is not effective on its business date.", nameof(lines));

            ValidateAmount(draft.Amount, nameof(lines));
            CollectionLine.ValidateSource(draft.SourceKind, draft.SourceId, draft.SourcePart);

            total += draft.Amount;
            if (total > MaximumMoneyAmount)
                throw new ArgumentOutOfRangeException(nameof(lines), "Collection total exceeds numeric(18,2) capacity.");

            collection._lines.Add(CollectionLine.Create(
                municipalityId,
                collection.Id,
                draft.Classification.Id,
                draft.Policy.Id,
                draft.Amount,
                draft.SourceKind,
                draft.SourceId,
                draft.SourcePart,
                draft.CalculationSnapshot,
                draft.Allocations));
        }

        collection.TotalAmount = total;
        return collection;
    }

    private static void ValidateAmount(decimal amount, string parameterName)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Collection line amounts must be positive.");
        if (amount > MaximumMoneyAmount)
            throw new ArgumentOutOfRangeException(parameterName, "Collection line amount exceeds numeric(18,2) capacity.");
        if (decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            throw new ArgumentOutOfRangeException(parameterName, "Collection line amounts cannot have more than two decimal places.");
    }

    private static void ValidateRequiredSnapshot(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new ArgumentException($"{parameterName} is required and must not exceed {maxLength} characters.", parameterName);
    }

    private static void ValidateOptionalId(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{parameterName} must be a valid identifier when supplied.", parameterName);
    }
}
