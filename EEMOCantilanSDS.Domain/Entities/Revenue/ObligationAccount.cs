using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// A lightweight, specialized receivable account for the operations that are billed as an approved amount to a
/// Business Payor but are not permanent stall tenancy (Fish/Meat Vendor Fee, Kanmanggay space rental, event lot
/// rental). It is a source, not a registry: the Payor is explicit, and a vendor-fee account points at the NPM stall that
/// supplies the vendor context instead of duplicating it. Amounts live in append-only <see cref="ObligationRate"/>
/// versions and assessed months in <see cref="ObligationPeriod"/>; money is only ever settled by canonical allocations.
/// </summary>
public sealed class ObligationAccount : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public ObligationKind Kind { get; private set; }
    public Guid? PayorId { get; private set; }
    /// <summary>Explicit source-owned holder snapshot. It never creates or matches a Business Payor.</summary>
    public string? ActualOccupant { get; private set; }

    /// <summary>The NPM Fish/Meat stall that supplies the vendor context. Only for the vendor fee kind.</summary>
    public Guid? StallId { get; private set; }

    /// <summary>The space, lot or location the account is for, as the Head names it.</summary>
    public string SubjectLabel { get; private set; } = string.Empty;

    public LotRentalEvent? Event { get; private set; }

    /// <summary>The event date of a lot rental. It is the rental's business date, not an automatic billing date.</summary>
    public DateOnly? EventDate { get; private set; }

    public DateOnly ActiveFrom { get; private set; }
    public DateOnly? ActiveTo { get; private set; }
    /// <summary>Optional documented space occupancy basis; null preserves older unspecified evidence.</summary>
    public OccupancyArrangement? Arrangement { get; private set; }
    public string? ContractReference { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private ObligationAccount() { }

    public bool IsMonthly => Kind != ObligationKind.FiestaArawLotRental;

    public bool IsActiveOn(DateOnly date) => date >= ActiveFrom && (ActiveTo is null || date <= ActiveTo);

    public static ObligationAccount Create(
        Guid municipalityId, ObligationKind kind, Guid payorId, Guid? stallId, string subjectLabel,
        LotRentalEvent? lotEvent, DateOnly? eventDate, DateOnly activeFrom, string createdBy, DateTime? createdAtUtc = null,
        string? actualOccupant = null)
    {
        var holder = string.IsNullOrWhiteSpace(actualOccupant) ? null : actualOccupant.Trim();
        if (municipalityId == Guid.Empty || holder?.Length > 200 || payorId == Guid.Empty &&
            (holder is null || kind is not (ObligationKind.KanmanggaySpaceRental or ObligationKind.FiestaArawLotRental)))
            throw new ArgumentException("A tenant and an explicit source holder are required.");
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        var label = (subjectLabel ?? string.Empty).Trim();
        if (label.Length is 0 or > 120)
            throw new ArgumentException("A space, lot or location label of 1-120 characters is required.", nameof(subjectLabel));
        if (kind == ObligationKind.FishMeatVendorFee && stallId is null)
            throw new ArgumentException("A vendor fee is anchored to the NPM Fish/Meat stall that supplies the vendor context.", nameof(stallId));
        if (kind != ObligationKind.FishMeatVendorFee && stallId is not null)
            throw new ArgumentException("Only a vendor fee is linked to an NPM stall.", nameof(stallId));
        if (kind == ObligationKind.FiestaArawLotRental)
        {
            if (lotEvent is null || !Enum.IsDefined(lotEvent.Value) || eventDate is null)
                throw new ArgumentException("A lot rental needs its event and event date.");
        }
        else if (lotEvent is not null || eventDate is not null)
            throw new ArgumentException("Only a lot rental carries an event.");
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
            throw new ArgumentException("The configuring actor is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));

        return new ObligationAccount
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            Kind = kind,
            PayorId = payorId == Guid.Empty ? null : payorId,
            ActualOccupant = holder,
            StallId = stallId,
            SubjectLabel = label,
            Event = lotEvent,
            EventDate = eventDate,
            ActiveFrom = kind == ObligationKind.FiestaArawLotRental ? eventDate!.Value : activeFrom,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }

    /// <summary>Ends the account. Assessed periods and their collections are history and are never touched.</summary>
    public void Close(DateOnly activeTo)
    {
        if (activeTo < ActiveFrom)
            throw new ArgumentException("An account cannot end before it begins.", nameof(activeTo));
        ActiveTo = activeTo;
    }

    public void SetOccupancyBasis(OccupancyArrangement? arrangement, string? contractReference)
    {
        var reference = string.IsNullOrWhiteSpace(contractReference) ? null : contractReference.Trim();
        if (arrangement is null && reference is null) return;
        if (Kind is not (ObligationKind.KanmanggaySpaceRental or ObligationKind.FiestaArawLotRental))
            throw new ArgumentException("Occupancy basis is only supported for space and event lot accounts.");
        if (arrangement is not (OccupancyArrangement.SignedContract or OccupancyArrangement.SpaceOnly))
            throw new ArgumentException("Choose signed contract or space only.");
        if (reference?.Length > 200)
            throw new ArgumentException("A contract reference must not exceed 200 characters.");
        if (arrangement == OccupancyArrangement.SpaceOnly && reference is not null)
            throw new ArgumentException("Space-only occupancy has no contract reference.");
        Arrangement = arrangement;
        ContractReference = reference;
    }

    /// <summary>
    /// The period starts an account may be billed for, up to and including the business date's month. A monthly account
    /// bills each calendar month it is active in; a lot rental has exactly one period, on its event date.
    /// </summary>
    public IReadOnlyList<DateOnly> PeriodStarts(DateOnly businessDate)
    {
        if (businessDate < ActiveFrom) return [];
        if (!IsMonthly)
            return EventDate is { } date && date <= businessDate ? [date] : [];

        var starts = new List<DateOnly>();
        var first = new DateOnly(ActiveFrom.Year, ActiveFrom.Month, 1);
        var lastDay = ActiveTo is { } to && to < businessDate ? to : businessDate;
        var last = new DateOnly(lastDay.Year, lastDay.Month, 1);
        for (var month = first; month <= last; month = month.AddMonths(1))
            starts.Add(month);
        return starts;
    }

    /// <summary>The first monthly liability uses the approved rate at actual occupancy start, never a backdated rate.</summary>
    public DateOnly RateAsOf(DateOnly periodStart) => Kind == ObligationKind.KanmanggaySpaceRental && periodStart.Year == ActiveFrom.Year &&
        periodStart.Month == ActiveFrom.Month && periodStart < ActiveFrom ? ActiveFrom : periodStart;

    public static string ClassificationCodeFor(ObligationKind kind) => kind switch
    {
        ObligationKind.FishMeatVendorFee => RevenueClassificationCodes.FishMeatVendorFee,
        ObligationKind.KanmanggaySpaceRental => RevenueClassificationCodes.KanmanggaySpaceRental,
        ObligationKind.FiestaArawLotRental => RevenueClassificationCodes.FiestaArawLotRental,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

/// <summary>
/// One immutable, effective-dated approved amount for an account (a monthly amount, or a lot's approved amount). A change
/// appends a new version; assessed periods freeze the version they used.
/// </summary>
public sealed class ObligationRate : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid ObligationAccountId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private ObligationRate() { }

    public static ObligationRate Create(
        Guid municipalityId, Guid accountId, DateOnly effectiveFrom, decimal amount, string createdBy, DateTime? createdAtUtc = null)
    {
        if (municipalityId == Guid.Empty || accountId == Guid.Empty)
            throw new ArgumentException("A tenant and an account are required.");
        if (amount <= 0m || amount > 10_000_000m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("An approved amount must be a positive figure in whole centavos.", nameof(amount));
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
            throw new ArgumentException("The configuring actor is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));
        return new ObligationRate
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            ObligationAccountId = accountId,
            EffectiveFrom = effectiveFrom,
            Amount = amount,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }

    /// <summary>The version in force on a date: the latest one effective on or before it, or null when none is.</summary>
    public static ObligationRate? Resolve(IEnumerable<ObligationRate> versions, DateOnly asOf) =>
        versions.Where(x => x.EffectiveFrom <= asOf)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.CreatedAtUtc).FirstOrDefault();
}

/// <summary>
/// One assessed period of an account (a calendar month, or a lot's single rental). The assessed amount is frozen from the
/// rate version in force at assessment and never recomputed. Collected and remaining amounts are not stored: they are
/// the assessed amount less the canonical allocations against this period, so there is one money authority.
/// </summary>
public sealed class ObligationPeriod : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid ObligationAccountId { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public decimal AssessedAmount { get; private set; }
    public Guid ObligationRateId { get; private set; }

    /// <summary>Bumped by every canonical posting against this period so two concurrent drafts cannot both settle it.</summary>
    public long SettlementVersion { get; private set; } = 1;

    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private ObligationPeriod() { }

    public static ObligationPeriod Assess(
        ObligationAccount account, ObligationRate rate, DateOnly periodStart, string createdBy, DateTime? createdAtUtc = null)
    {
        if (rate.ObligationAccountId != account.Id || rate.MunicipalityId != account.MunicipalityId)
            throw new ArgumentException("The rate does not belong to this account.", nameof(rate));
        if (rate.EffectiveFrom > account.RateAsOf(periodStart))
            throw new ArgumentException("The approved rate is not yet in force for this period.", nameof(rate));
        var inWindow = account.IsMonthly
            ? periodStart.Day == 1 && account.PeriodStarts(account.RateAsOf(periodStart)).Contains(periodStart)
            : account.EventDate == periodStart;
        if (!inWindow)
            throw new ArgumentException("The period is outside the account's active window.", nameof(periodStart));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));
        return new ObligationPeriod
        {
            Id = Guid.NewGuid(),
            MunicipalityId = account.MunicipalityId,
            ObligationAccountId = account.Id,
            PeriodStart = periodStart,
            AssessedAmount = rate.Amount,
            ObligationRateId = rate.Id,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }

    public void ApplyCanonicalSettlement() => SettlementVersion = checked(SettlementVersion + 1);
}
