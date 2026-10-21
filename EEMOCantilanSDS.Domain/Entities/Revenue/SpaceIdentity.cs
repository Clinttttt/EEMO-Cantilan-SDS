using System.Globalization;
using System.Numerics;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// The stable identity of a physical monthly space (Kanmanggay or Vegetable/Fruit): the thing that outlives any one occupant, term or
/// account. Each <see cref="ObligationAccount"/> on it is one occupancy. Holders are never matched by name: two accounts are on the same
/// space only when an authorized lifecycle action (or the unambiguous backfill) linked them to the same registration.
/// </summary>
public sealed class SpaceRegistration : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public ObligationKind Kind { get; private set; }
    /// <summary>The number as the office writes it (e.g. "12", "A-3").</summary>
    public string DisplayNumber { get; private set; } = string.Empty;
    /// <summary>The comparison form: plain integers lose leading zeros, anything else is trimmed and upper-cased. Unique per tenant and kind.</summary>
    public string NormalizedNumber { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private SpaceRegistration() { }

    /// <summary>The same normalization the numbering allocator uses, so a registration and a number reservation never disagree.</summary>
    public static string Normalize(string label) =>
        BigInteger.TryParse(label.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number.ToString(CultureInfo.InvariantCulture) : label.Trim().ToUpperInvariant();

    public static SpaceRegistration Create(Guid municipalityId, ObligationKind kind, string displayNumber, string createdBy, DateTime? createdAtUtc = null)
    {
        if (municipalityId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(municipalityId));
        if (!ObligationAccount.IsMonthlySpaceKind(kind)) throw new ArgumentException("Only a monthly space has a physical-space registration.", nameof(kind));
        var label = (displayNumber ?? string.Empty).Trim();
        if (label.Length is 0 or > 120) throw new ArgumentException("A space number of 1-120 characters is required.", nameof(displayNumber));
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100) throw new ArgumentException("The acting user is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc) throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));
        return new SpaceRegistration
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, Kind = kind, DisplayNumber = label,
            NormalizedNumber = Normalize(label), CreatedAtUtc = created, CreatedBy = createdBy.Trim()
        };
    }
}

public enum SpaceLifecycleAction
{
    Resume = 1,
    Renew = 2,
    Reassign = 3,
    Retire = 4,
    /// <summary>Recorded when the unambiguous backfill gave an existing account its registration. Never a successor link.</summary>
    Backfill = 5
}

/// <summary>
/// An appended contract term for a space occupancy (a renewal). The account's own descriptive lease fields remain the first term; a renewal
/// adds one of these, linked to the term it follows. Append-only: nothing here changes a rate, an assessment or a Collection.
/// </summary>
public sealed class SpaceContractTerm : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid ObligationAccountId { get; private set; }
    public Guid SpaceRegistrationId { get; private set; }
    /// <summary>The term this one follows; null when it follows the account's own descriptive term.</summary>
    public Guid? PriorTermId { get; private set; }
    public DateOnly Commencement { get; private set; }
    public int Years { get; private set; }
    public string? ContractName { get; private set; }
    public string? Reference { get; private set; }
    /// <summary>The office's approval evidence (resolution, signed document reference).</summary>
    public string ApprovalEvidence { get; private set; } = string.Empty;
    public Guid LifecycleEventId { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }

    private SpaceContractTerm() { }

    /// <summary>The last day the term covers, by the same rule as every other term (<see cref="DomainRules.TermLastDay"/>).</summary>
    public DateOnly LastDay => DomainRules.TermLastDay(Commencement, Years);

    public static SpaceContractTerm Create(Guid municipalityId, Guid accountId, Guid registrationId, Guid? priorTermId, DateOnly commencement,
        int years, string? contractName, string? reference, string approvalEvidence, Guid lifecycleEventId, Guid actorId, string actorName, DateTime recordedAtUtc)
    {
        if (municipalityId == Guid.Empty || accountId == Guid.Empty || registrationId == Guid.Empty || lifecycleEventId == Guid.Empty)
            throw new ArgumentException("A tenant, account, registration and lifecycle event are required.");
        if (years is < 1 or > 50) throw new ArgumentException("A term runs 1-50 years.", nameof(years));
        if (string.IsNullOrWhiteSpace(approvalEvidence) || approvalEvidence.Trim().Length > 500) throw new ArgumentException("Approval evidence of up to 500 characters is required.", nameof(approvalEvidence));
        if (contractName?.Length > 200 || reference?.Length > 200) throw new ArgumentException("Name and reference are limited to 200 characters.");
        if (string.IsNullOrWhiteSpace(actorName) || actorName.Length > 100 || recordedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("A valid actor and UTC time are required.");
        return new SpaceContractTerm
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, ObligationAccountId = accountId, SpaceRegistrationId = registrationId,
            PriorTermId = priorTermId, Commencement = commencement, Years = years,
            ContractName = string.IsNullOrWhiteSpace(contractName) ? null : contractName.Trim(),
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            ApprovalEvidence = approvalEvidence.Trim(), LifecycleEventId = lifecycleEventId, ActorId = actorId, ActorName = actorName.Trim(), RecordedAtUtc = recordedAtUtc
        };
    }
}

/// <summary>
/// Append-only evidence of one lifecycle action on a physical space, and the durable idempotency record for it: the tenant-unique
/// <see cref="ClientOperationId"/> with its <see cref="IntentFingerprint"/> makes a retry return the original outcome and a changed
/// request under the same id a conflict. The database refuses UPDATE and DELETE on this table.
/// </summary>
public sealed class SpaceLifecycleEvent : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid SpaceRegistrationId { get; private set; }
    public SpaceLifecycleAction Action { get; private set; }
    /// <summary>The account the action was taken on.</summary>
    public Guid SourceAccountId { get; private set; }
    /// <summary>The account the action created (a successor occupancy), when it created one.</summary>
    public Guid? ResultAccountId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public Guid ClientOperationId { get; private set; }
    public string IntentFingerprint { get; private set; } = string.Empty;
    /// <summary>The state version the actor confirmed in the preview.</summary>
    public string PreviewVersion { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string? Evidence { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }

    private SpaceLifecycleEvent() { }

    public static SpaceLifecycleEvent Record(Guid municipalityId, Guid registrationId, SpaceLifecycleAction action, Guid sourceAccountId,
        Guid? resultAccountId, DateOnly effectiveDate, Guid clientOperationId, string fingerprint, string previewVersion, string reason,
        string? evidence, Guid actorId, string actorName, DateTime recordedAtUtc)
    {
        if (municipalityId == Guid.Empty || registrationId == Guid.Empty || sourceAccountId == Guid.Empty || clientOperationId == Guid.Empty)
            throw new ArgumentException("A tenant, registration, account and operation id are required.");
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (fingerprint?.Length != 64 || previewVersion?.Length is null or 0 or > 64) throw new ArgumentException("A request fingerprint and preview version are required.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 || evidence?.Length > 500) throw new ArgumentException("A reason of up to 500 characters is required.");
        if (string.IsNullOrWhiteSpace(actorName) || actorName.Length > 100 || recordedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("A valid actor and UTC time are required.");
        return new SpaceLifecycleEvent
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, SpaceRegistrationId = registrationId, Action = action,
            SourceAccountId = sourceAccountId, ResultAccountId = resultAccountId, EffectiveDate = effectiveDate,
            ClientOperationId = clientOperationId, IntentFingerprint = fingerprint, PreviewVersion = previewVersion,
            Reason = reason.Trim(), Evidence = string.IsNullOrWhiteSpace(evidence) ? null : evidence.Trim(),
            ActorId = actorId, ActorName = actorName.Trim(), RecordedAtUtc = recordedAtUtc
        };
    }
}
