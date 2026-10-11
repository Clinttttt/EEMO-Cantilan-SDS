using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

public enum FineAccountKind { FacilityContract = 1, Kanmanggay = 2, VegetableFruitMonthly = 3, FiestaAraw = 4 }
public enum FinePenaltyDateBasis { AssessmentDate = 1, ViolationDate = 2 }
public enum FineAffectedPeriodKind { FacilityMonth = 1, NpmDay = 2, ObligationPeriod = 3 }

/// <summary>Explicit governing evidence for one definition version/source combination; never seeded by inference.</summary>
public sealed class FineAssessmentPolicy : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid PenaltyVersionId { get; private set; }
    public FineAccountKind AccountKind { get; private set; }
    public FacilityCode? Facility { get; private set; }
    public string GoverningReference { get; private set; } = "";
    public FinePenaltyDateBasis DateBasis { get; private set; }
    public bool AllowsUncappedAmount { get; private set; }
    public bool AllowsClosedAccount { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    private FineAssessmentPolicy() { }
    public static FineAssessmentPolicy Create(Guid tenant, Guid version, FineAccountKind kind, FacilityCode? facility,
        string reference, FinePenaltyDateBasis basis, bool uncapped, bool closed, Guid actor, DateTime now)
    {
        if (tenant == Guid.Empty || version == Guid.Empty || actor == Guid.Empty || !Enum.IsDefined(kind) || !Enum.IsDefined(basis))
            throw new ArgumentException("Valid tenant, actor, definition and source policy are required.");
        if (kind == FineAccountKind.FacilityContract != facility.HasValue || facility.HasValue && !Enum.IsDefined(facility.Value))
            throw new ArgumentException("Select the actual facility for a facility-contract policy.");
        return new() { Id = Guid.NewGuid(), MunicipalityId = tenant, PenaltyVersionId = version, AccountKind = kind,
            Facility = facility, GoverningReference = FineAssessment.Required(reference, 200, "Governing reference"),
            DateBasis = basis, AllowsUncappedAmount = uncapped, AllowsClosedAccount = closed, ActorId = actor,
            RecordedAtUtc = now.Kind == DateTimeKind.Utc ? now : throw new ArgumentException("UTC timestamp required.") };
    }
}

/// <summary>Non-cash liability. Immutable original principal/evidence; cash settlement is tracked through canonical allocations.</summary>
public sealed class FineAssessment : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid? ContractId { get; private set; }
    public Guid? ObligationAccountId { get; private set; }
    public FineAccountKind AccountKind { get; private set; }
    public Guid PolicyId { get; private set; }
    public Guid PenaltyVersionId { get; private set; }
    public string PenaltyCode { get; private set; } = "";
    public string PenaltyName { get; private set; } = "";
    public string Occupant { get; private set; } = "";
    public string UnitReference { get; private set; } = "";
    public string SourceName { get; private set; } = "";
    public string AuthoritySnapshot { get; private set; } = "";
    public FineAffectedPeriodKind? RelatedPeriodKind { get; private set; }
    public DateOnly? RelatedPeriodStart { get; private set; }
    public DateOnly? RelatedPeriodEnd { get; private set; }
    public Guid? RelatedObligationPeriodId { get; private set; }
    public string? RelatedPeriodLabel { get; private set; }
    public DateOnly ViolationDate { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string? IncidentReference { get; private set; }
    public string Description { get; private set; } = "";
    public decimal AssessedAmount { get; private set; }
    public Guid ClientOperationId { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public Guid ActorId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public long Version { get; private set; } = 1;
    private FineAssessment() { }
    /// <summary>Serializes canonical settlement claims; this is a concurrency token, not an amount-paid counter.</summary>
    public void ClaimSettlement(long expectedVersion)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The fine assessment changed. Obtain a new quote.");
        Version = checked(Version + 1);
    }
    public static FineAssessment Create(Guid tenant, FineAccountKind kind, Guid accountId, FineAssessmentPolicy policy,
        PenaltyDefinition penalty, string occupant, string unit, string source, DateOnly violation, DateOnly businessDate,
        string? incident, string description, decimal amount, Guid operationId, string fingerprint, Guid actor,
        DateTime now, string authoritySnapshot, FineAffectedPeriodKind? periodKind = null, DateOnly? periodStart = null,
        DateOnly? periodEnd = null, Guid? obligationPeriodId = null, string? periodLabel = null)
    {
        if (tenant == Guid.Empty || actor == Guid.Empty || accountId == Guid.Empty || operationId == Guid.Empty)
            throw new ArgumentException("Tenant, account, actor and operation identity are required.");
        if (policy.MunicipalityId != tenant || penalty.MunicipalityId != tenant || policy.AccountKind != kind || policy.PenaltyVersionId != penalty.Id)
            throw new ArgumentException("The governing policy does not belong to this assessment.");
        if (penalty.CheckAmount(amount) is { } problem) throw new ArgumentException(problem);
        if (penalty.Basis == GovernedServiceBasis.DirectApprovedAmount && penalty.MaximumAmount is null && !policy.AllowsUncappedAmount)
            throw new ArgumentException("Uncapped amounts require explicit governing authorization.");
        if (violation > businessDate || now.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid violation date or timestamp.");
        if (periodKind is not null && (!Enum.IsDefined(periodKind.Value) || periodStart is null || periodEnd is null || periodEnd < periodStart ||
            (periodKind == FineAffectedPeriodKind.ObligationPeriod) != obligationPeriodId.HasValue))
            throw new ArgumentException("Invalid related source period.");
        if (periodKind is null && (periodStart is not null || periodEnd is not null || obligationPeriodId is not null || periodLabel is not null))
            throw new ArgumentException("Related period evidence requires a typed period.");
        var normalizedIncident = string.IsNullOrWhiteSpace(incident) ? null : Required(incident, 200, "Incident reference").ToUpperInvariant();
        return new() { Id = Guid.NewGuid(), MunicipalityId = tenant, AccountKind = kind,
            ContractId = kind == FineAccountKind.FacilityContract ? accountId : null,
            ObligationAccountId = kind != FineAccountKind.FacilityContract ? accountId : null,
            PolicyId = policy.Id, PenaltyVersionId = penalty.Id, PenaltyCode = penalty.Code, PenaltyName = penalty.DisplayName,
            Occupant = Required(occupant, 200, "Liable occupant"), UnitReference = Required(unit, 120, "Unit"),
            SourceName = Required(source, 120, "Source"), AuthoritySnapshot = Required(authoritySnapshot, 16384, "Authority evidence"),
            ViolationDate = violation, BusinessDate = businessDate, IncidentReference = normalizedIncident,
            Description = Required(description, 1000, "Description"), AssessedAmount = amount, ClientOperationId = operationId,
            RequestFingerprint = Required(fingerprint, 64, "Fingerprint"), ActorId = actor, RecordedAtUtc = now,
            RelatedPeriodKind = periodKind, RelatedPeriodStart = periodStart, RelatedPeriodEnd = periodEnd,
            RelatedObligationPeriodId = obligationPeriodId, RelatedPeriodLabel = periodKind is null ? null : Required(periodLabel, 160, "Related period") };
    }
    internal static string Required(string? value, int limit, string field) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= limit
        ? value.Trim() : throw new ArgumentException($"{field} is required and must not exceed {limit} characters.");
}

/// <summary>Append-only assessment evidence. Adjustments remain disabled until their cash/obligation contract is verified.</summary>
public sealed class FineAssessmentEvent : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid AssessmentId { get; private set; }
    public string EventKind { get; private set; } = "Created";
    public decimal PreviousAmount { get; private set; }
    public decimal ResultingAmount { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string Evidence { get; private set; } = "";
    private FineAssessmentEvent() { }
    public static FineAssessmentEvent Created(FineAssessment assessment) => new() { Id = Guid.NewGuid(),
        MunicipalityId = assessment.MunicipalityId, AssessmentId = assessment.Id, ResultingAmount = assessment.AssessedAmount,
        ActorId = assessment.ActorId, RecordedAtUtc = assessment.RecordedAtUtc, Evidence = assessment.AuthoritySnapshot };
}
