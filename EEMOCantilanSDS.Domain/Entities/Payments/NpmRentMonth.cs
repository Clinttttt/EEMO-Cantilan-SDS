using System.Text.Json;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Payments;

/// <summary>
/// Source identity/evidence for one NPM rent month. Preparation/reconciliation never imports cash or authorizes
/// collection. The candidate financial activation state additionally requires the coordinated runtime/schema gate.
/// Application callers must authorize the reviewer and save returned audit evidence atomically with changes.
/// </summary>
public sealed class NpmRentMonth : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid FacilityId { get; private set; }
    public Guid StallId { get; private set; }
    public Guid ContractId { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly OwnershipAsOf { get; private set; }
    public decimal AssessedAmount { get; private set; }
    public decimal CreditedAmount { get; private set; }
    public NpmMonthBasis? MonthBasis { get; private set; }
    public DateOnly RateAsOf { get; private set; }
    public string PolicyReference { get; private set; } = string.Empty;
    public string RateEvidenceJson { get; private set; } = string.Empty;
    public string CreditEvidenceJson { get; private set; } = string.Empty;
    public NpmRentMonthPreparationStatus Status { get; private set; }
    public long SettlementVersion { get; private set; } = 1;
    public Guid CreatedByUserId { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public string? LastRevisionReason { get; private set; }
    public Guid? ReconciledByUserId { get; private set; }
    public DateTime? ReconciledAtUtc { get; private set; }
    public string? ReconciliationEvidence { get; private set; }
    /// <summary>First canonical month allocation freezes assessment provenance permanently, including after reversal.</summary>
    public DateTime? SettlementSealedAtUtc { get; private set; }
    public DateTime? SettlementActivatedAtUtc { get; private set; }
    public Guid? SettlementActivatedByUserId { get; private set; }

    /// <summary>Review alone is never activation. The runtime/schema/authorization boundaries are also required.</summary>
    public bool CanAcceptCollections => Status == NpmRentMonthPreparationStatus.FinanciallyActivated;

    private NpmRentMonth() { }

    /// <summary>
    /// Creates one proposed source and its audit record. Uniqueness is enforced by the relational key, not by this
    /// factory. A different ContractId cannot create a second source for the same tenant/stall/calendar month.
    /// </summary>
    public static (NpmRentMonth Month, AuditLog Audit) Prepare(
        Facility facility, Stall stall, Contract contract, DateOnly periodStart, DateOnly ownershipAsOf,
        NpmRentMonthAssessmentEvidence assessment, Guid actorUserId, string actor, string actorRole, DateTime recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(facility);
        ArgumentNullException.ThrowIfNull(stall);
        ArgumentNullException.ThrowIfNull(contract);
        if (periodStart.Day != 1)
            throw new ArgumentException("The assessment period must start on the first day of a month.", nameof(periodStart));
        ValidateActor(actorUserId, actor, actorRole, recordedAtUtc);
        ValidateAssessment(assessment);
        if (facility.Code != FacilityCode.NPM || facility.IsDeleted || stall.IsDeleted || contract.IsDeleted
            || facility.Id == Guid.Empty || stall.Id == Guid.Empty || contract.Id == Guid.Empty
            || facility.MunicipalityId == Guid.Empty || stall.MunicipalityId != facility.MunicipalityId
            || contract.MunicipalityId != facility.MunicipalityId || stall.FacilityId != facility.Id
            || contract.StallId != stall.Id || !stall.Contracts.Any(c => c.Id == contract.Id))
            throw new ArgumentException("A verified same-tenant NPM facility, stall and its recorded contract are required.");

        var windows = stall.Occupancies(ownershipAsOf);
        var owner = StallOccupancy.AnsweringForMonth(windows, periodStart.Year, periodStart.Month);
        if (owner is null || owner.Contract.Id != contract.Id
            || stall.Contracts.Count(c => !c.IsDeleted && c.EffectivityDate == owner.Start) != 1)
            throw new InvalidOperationException("Month ownership is missing, conflicting or ambiguous; reconcile it explicitly before preparation.");

        var month = new NpmRentMonth
        {
            MunicipalityId = facility.MunicipalityId, FacilityId = facility.Id, StallId = stall.Id,
            ContractId = contract.Id, PeriodStart = periodStart, OwnershipAsOf = ownershipAsOf,
            Status = NpmRentMonthPreparationStatus.NeedsReconciliation,
            CreatedByUserId = actorUserId, CreatedBy = actor.Trim(), CreatedAtUtc = recordedAtUtc
        };
        month.SetAssessment(assessment);
        return (month, month.Audit("PrepareNpmRentMonth", null, actorUserId, actor, actorRole, "Preparation only; no cash or financial activation."));
    }

    /// <summary>
    /// Replaces approved evidence, never identity or cash. Invalidates reconciliation. The future application
    /// command must authorize the actor and persist the returned old/new AuditLog in the same transaction.
    /// </summary>
    public AuditLog ReviseAssessment(NpmRentMonthAssessmentEvidence assessment, long expectedVersion,
        Guid actorUserId, string actor, string actorRole, DateTime recordedAtUtc, string reason)
    {
        if (SettlementSealedAtUtc is not null || CanAcceptCollections)
            throw new InvalidOperationException("Posted month assessment evidence is immutable; an authorized separate correction is required.");
        ValidateChange(expectedVersion, actorUserId, actor, actorRole, recordedAtUtc, reason);
        ValidateAssessment(assessment);
        var nextVersion = checked(SettlementVersion + 1);
        var before = Snapshot();
        SetAssessment(assessment);
        Status = NpmRentMonthPreparationStatus.NeedsReconciliation;
        ReconciledByUserId = null; ReconciledAtUtc = null; ReconciliationEvidence = null;
        StampChange(nextVersion, actorUserId, actor, recordedAtUtc, reason);
        return Audit("ReviseNpmRentMonthAssessment", before, actorUserId, actor, actorRole, reason);
    }

    /// <summary>Records review only. It neither imports an opening cash balance nor enables a payment writer.</summary>
    public AuditLog Reconcile(long expectedVersion, Guid actorUserId, string actor, string actorRole, DateTime recordedAtUtc, string evidence)
    {
        ValidateChange(expectedVersion, actorUserId, actor, actorRole, recordedAtUtc, evidence);
        if (MonthBasis is null)
            throw new InvalidOperationException("Unknown historical month basis requires evidenced assessment revision before reconciliation.");
        if (Status != NpmRentMonthPreparationStatus.NeedsReconciliation)
            throw new InvalidOperationException("This evidence is already reconciled; revise it explicitly before a new review.");
        var nextVersion = checked(SettlementVersion + 1);
        var before = Snapshot();
        Status = NpmRentMonthPreparationStatus.Reconciled;
        ReconciledByUserId = actorUserId; ReconciledAtUtc = recordedAtUtc; ReconciliationEvidence = evidence.Trim();
        StampChange(nextVersion, actorUserId, actor, recordedAtUtc, evidence);
        return Audit("ReconcileNpmRentMonthPreparation", before, actorUserId, actor, actorRole, evidence);
    }

    /// <summary>Candidate cutover action; no production endpoint invokes it. Save its returned audit atomically.</summary>
    public AuditLog ActivateSettlement(long expectedVersion, Guid actorUserId, string actor, string actorRole,
        DateTime recordedAtUtc, string evidence)
    {
        ValidateChange(expectedVersion, actorUserId, actor, actorRole, recordedAtUtc, evidence);
        if (actorRole is not ("SuperAdmin" or "Admin") || Status != NpmRentMonthPreparationStatus.Reconciled || MonthBasis is null)
            throw new InvalidOperationException("An authorized Head must explicitly activate reconciled month evidence.");
        var before = Snapshot();
        Status = NpmRentMonthPreparationStatus.FinanciallyActivated;
        SettlementActivatedAtUtc = recordedAtUtc; SettlementActivatedByUserId = actorUserId;
        StampChange(checked(SettlementVersion + 1), actorUserId, actor, recordedAtUtc, evidence);
        return Audit("ActivateNpmRentMonthSettlement", before, actorUserId, actor, actorRole, evidence);
    }

    /// <summary>Save atomically with the authorized canonical allocation/correction and its audit.</summary>
    public AuditLog RecordSettlementChange(long expectedVersion, Guid actorUserId, string actor, string actorRole,
        DateTime recordedAtUtc, string operationEvidence)
    {
        ValidateChange(expectedVersion, actorUserId, actor, actorRole, recordedAtUtc, operationEvidence);
        if (!CanAcceptCollections || MonthBasis is null)
            throw new InvalidOperationException("An explicitly activated financial month source is required.");
        var before = Snapshot();
        SettlementSealedAtUtc ??= recordedAtUtc;
        StampChange(checked(SettlementVersion + 1), actorUserId, actor, recordedAtUtc, operationEvidence);
        return Audit("NpmRentMonthSettlementRevision", before, actorUserId, actor, actorRole, operationEvidence);
    }

    /// <summary>Head-approved credit evidence changes liability, never original assessment, cash or receipt evidence.</summary>
    public AuditLog ReviseCredits(decimal approvedCredits, string creditEvidenceJson, long expectedVersion,
        Guid actorUserId, string actor, string actorRole, DateTime recordedAtUtc, string reason, Guid? operationId = null)
    {
        if (!CanAcceptCollections || actorRole is not ("SuperAdmin" or "Admin"))
            throw new InvalidOperationException("An activated month and authorized Head credit decision are required.");
        ValidateChange(expectedVersion, actorUserId, actor, actorRole, recordedAtUtc, reason);
        var evidence = new NpmRentMonthAssessmentEvidence(AssessedAmount, approvedCredits, MonthBasis,
            RateAsOf, PolicyReference, RateEvidenceJson, creditEvidenceJson);
        ValidateAssessment(evidence);
        var before = Snapshot();
        CreditedAmount = approvedCredits; CreditEvidenceJson = creditEvidenceJson;
        StampChange(checked(SettlementVersion + 1), actorUserId, actor, recordedAtUtc, reason);
        return Audit("ReviseNpmRentMonthCredits", before, actorUserId, actor, actorRole, reason, operationId);
    }

    private void ValidateChange(long expectedVersion, Guid actorUserId, string actor, string actorRole, DateTime utc, string reason)
    {
        if (expectedVersion != SettlementVersion)
            throw new InvalidOperationException("The month evidence changed; reload before reviewing or revising it.");
        ValidateActor(actorUserId, actor, actorRole, utc);
        if (utc < (UpdatedAtUtc ?? CreatedAtUtc))
            throw new ArgumentException("A revision cannot predate the recorded evidence.", nameof(utc));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 4000)
            throw new ArgumentException("An explicit review reason/evidence of at most 4000 characters is required.", nameof(reason));
    }

    private static void ValidateActor(Guid actorUserId, string actor, string actorRole, DateTime utc)
    {
        if (actorUserId == Guid.Empty || string.IsNullOrWhiteSpace(actor) || actor.Trim().Length > 100)
            throw new ArgumentException("A verified reviewer ID and name of at most 100 characters are required.");
        if (string.IsNullOrWhiteSpace(actorRole) || actorRole.Trim().Length > 50)
            throw new ArgumentException("The actual authenticated reviewer role is required.", nameof(actorRole));
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The evidence timestamp must be UTC.", nameof(utc));
    }

    private static void ValidateAssessment(NpmRentMonthAssessmentEvidence assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        const decimal maxMoney = 9_999_999_999_999_999.99m; // numeric(18,2), not a tariff or billing calculation
        if (assessment.AssessedAmount < 0m || assessment.AssessedAmount > maxMoney
            || assessment.CreditedAmount < 0m || assessment.CreditedAmount > assessment.AssessedAmount
            || decimal.Round(assessment.AssessedAmount, 2) != assessment.AssessedAmount
            || decimal.Round(assessment.CreditedAmount, 2) != assessment.CreditedAmount)
            throw new ArgumentOutOfRangeException(nameof(assessment), "Assessment and credits must be nonnegative cent amounts; credits cannot exceed assessment.");
        if (assessment.MonthBasis is { } basis && !Enum.IsDefined(basis))
            throw new ArgumentException("Month basis must be recorded accurately or remain unknown.", nameof(assessment));
        if (string.IsNullOrWhiteSpace(assessment.PolicyReference) || assessment.PolicyReference.Trim().Length > 1000)
            throw new ArgumentException("Approved policy or unresolved historical evidence reference is required.", nameof(assessment));
        ValidateJson(assessment.RateEvidenceJson);
        ValidateJson(assessment.CreditEvidenceJson);
    }

    private static void ValidateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16384)
            throw new ArgumentException("Bounded, explicit rate/credit provenance is required.", nameof(json));
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.EnumerateObject().Any())
                throw new ArgumentException("Evidence must be a nonempty JSON object.", nameof(json));
        }
        catch (JsonException e) { throw new ArgumentException("Evidence must be valid JSON.", nameof(json), e); }
    }

    private void SetAssessment(NpmRentMonthAssessmentEvidence assessment)
    {
        AssessedAmount = assessment.AssessedAmount; CreditedAmount = assessment.CreditedAmount;
        MonthBasis = assessment.MonthBasis; RateAsOf = assessment.RateAsOf;
        PolicyReference = assessment.PolicyReference.Trim();
        RateEvidenceJson = assessment.RateEvidenceJson; CreditEvidenceJson = assessment.CreditEvidenceJson;
    }

    private void StampChange(long version, Guid actorUserId, string actor, DateTime utc, string reason)
    {
        SettlementVersion = version; UpdatedByUserId = actorUserId; UpdatedBy = actor.Trim(); UpdatedAtUtc = utc;
        LastRevisionReason = reason.Trim();
    }

    private string Snapshot() => JsonSerializer.Serialize(new
    {
        Id, MunicipalityId, FacilityId, StallId, ContractId, PeriodStart, OwnershipAsOf,
        AssessedAmount, CreditedAmount, MonthBasis, RateAsOf, PolicyReference, RateEvidenceJson, CreditEvidenceJson,
        Status, SettlementVersion, CreatedByUserId, CreatedBy, CreatedAtUtc,
        UpdatedByUserId, UpdatedBy, UpdatedAtUtc, LastRevisionReason, ReconciledByUserId, ReconciledAtUtc, ReconciliationEvidence,
        SettlementSealedAtUtc, SettlementActivatedAtUtc, SettlementActivatedByUserId
    });

    private AuditLog Audit(string action, string? before, Guid actorUserId, string actor, string actorRole, string reason, Guid? eventId = null) =>
        AuditLog.Create(actorUserId.ToString(), actor.Trim(), actorRole.Trim(), action, nameof(NpmRentMonth), Id,
            before, Snapshot(), notes: reason.Trim(), municipalityId: MunicipalityId, eventId: eventId);
}

/// <summary>The first two states remain nonfinancial. State 3 is accepted only by the uninstalled financial schema candidate.</summary>
public enum NpmRentMonthPreparationStatus { NeedsReconciliation = 1, Reconciled = 2, FinanciallyActivated = 3 }

/// <summary>
/// Approved inputs from the specialized NPM rules/policy, not a second calculator. Provenance should capture
/// actual rate IDs/versions/effective dates, occupancy/policy evidence and credit sources; null basis is unknown.
/// </summary>
public sealed record NpmRentMonthAssessmentEvidence(decimal AssessedAmount, decimal CreditedAmount,
    NpmMonthBasis? MonthBasis, DateOnly RateAsOf, string PolicyReference, string RateEvidenceJson, string CreditEvidenceJson);
