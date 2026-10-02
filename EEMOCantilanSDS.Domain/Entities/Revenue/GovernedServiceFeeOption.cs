using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// One approved fee a governed service may collect (for example, under Market Fees, "Comfort Room — Transport Terminal").
/// The same shape as <see cref="VehicleClass"/>: a stable, tenant-owned identity that carries no money of its own; its
/// amount rule lives in append-only, effective-dated <see cref="GovernedServiceFeeOptionRate"/> versions. The option never
/// changes the service's revenue classification or accountable instrument. A collector selects an option; they never
/// name one, never price a fixed one, and never turn a fixed one into a typed amount. Retirement is prospective only.
/// </summary>
public sealed class GovernedServiceFeeOption : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid GovernedServiceId { get; private set; }

    /// <summary>Optional stable internal reference (e.g. CR_TERMINAL). Never the collector-facing label.</summary>
    public string? Code { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Optional location or context, e.g. "Transport Terminal".</summary>
    public string? Location { get; private set; }

    public string? Description { get; private set; }

    /// <summary>The first business date the option is no longer offered (null = offered). Never in the past.</summary>
    public DateOnly? RetiredFrom { get; private set; }
    public string? RetiredBy { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private GovernedServiceFeeOption() { }

    public static GovernedServiceFeeOption Create(
        Guid municipalityId, Guid governedServiceId, string displayName, string? code, string? location, string? description,
        string createdBy, DateTime? createdAtUtc = null)
    {
        if (municipalityId == Guid.Empty || governedServiceId == Guid.Empty)
            throw new ArgumentException("A tenant and a governed service are required.");
        var name = (displayName ?? string.Empty).Trim();
        if (name.Length is 0 or > 120)
            throw new ArgumentException("A display name of 1-120 characters is required.", nameof(displayName));
        string? normalizedCode = null;
        if (!string.IsNullOrWhiteSpace(code))
        {
            normalizedCode = code.Trim().ToUpperInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(normalizedCode, "^[A-Z][A-Z0-9_]{1,39}$"))
                throw new ArgumentException("The internal reference must be 2-40 letters, digits or underscores, starting with a letter.", nameof(code));
        }
        var place = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        if (place is { Length: > 80 })
            throw new ArgumentException("The location must not exceed 80 characters.", nameof(location));
        var note = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (note is { Length: > 300 })
            throw new ArgumentException("The description must not exceed 300 characters.", nameof(description));
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
            throw new ArgumentException("The configuring actor is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));
        return new GovernedServiceFeeOption
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            GovernedServiceId = governedServiceId,
            Code = normalizedCode,
            DisplayName = name,
            Location = place,
            Description = note,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }

    /// <summary>True when the option may be selected for a transaction on this business date.</summary>
    public bool IsOfferedOn(DateOnly businessDate) => RetiredFrom is not { } from || businessDate < from;

    /// <summary>Stops offering the option from a business date that is not in the past. History is untouched.</summary>
    public void Retire(DateOnly fromDate, DateOnly today, string actor)
    {
        if (fromDate < today)
            throw new ArgumentException("An option is retired prospectively, from today or a later date.", nameof(fromDate));
        if (RetiredFrom is { } existing && existing <= today)
            throw new InvalidOperationException("This fee option is already retired.");
        if (string.IsNullOrWhiteSpace(actor) || actor.Trim().Length > 100)
            throw new ArgumentException("The configuring actor is required.", nameof(actor));
        RetiredFrom = fromDate;
        RetiredBy = actor.Trim();
    }
}

/// <summary>
/// One immutable, effective-dated amount rule of a fee option: a fixed approved amount, or a direct amount entered at
/// collection (with an optional approved ceiling). A change appends a version; posted collection lines freeze the version
/// they used, so a later change never alters a recorded amount.
/// </summary>
public sealed class GovernedServiceFeeOptionRate : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid FeeOptionId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }

    /// <summary><see cref="GovernedServiceBasis.FixedAmount"/> or <see cref="GovernedServiceBasis.DirectApprovedAmount"/>.</summary>
    public GovernedServiceBasis Basis { get; private set; }

    public decimal? FixedAmount { get; private set; }
    public decimal? MaximumAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private GovernedServiceFeeOptionRate() { }

    public static GovernedServiceFeeOptionRate Create(
        Guid municipalityId, Guid feeOptionId, DateOnly effectiveDate, GovernedServiceBasis basis,
        decimal? fixedAmount, decimal? maximumAmount, string createdBy, DateTime? createdAtUtc = null)
    {
        if (municipalityId == Guid.Empty || feeOptionId == Guid.Empty)
            throw new ArgumentException("A tenant and a fee option are required.");
        switch (basis)
        {
            case GovernedServiceBasis.FixedAmount:
                ValidateMoney(fixedAmount ?? 0m, nameof(fixedAmount));
                if (maximumAmount is not null)
                    throw new ArgumentException("A fixed fee has no separate ceiling.", nameof(maximumAmount));
                break;
            case GovernedServiceBasis.DirectApprovedAmount:
                if (fixedAmount is not null)
                    throw new ArgumentException("A direct-amount fee has no fixed amount.", nameof(fixedAmount));
                if (maximumAmount is { } ceiling)
                    ValidateMoney(ceiling, nameof(maximumAmount));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(basis), "A fee option is either a fixed amount or a direct amount.");
        }
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
            throw new ArgumentException("The configuring actor is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));
        return new GovernedServiceFeeOptionRate
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            FeeOptionId = feeOptionId,
            EffectiveDate = effectiveDate,
            Basis = basis,
            FixedAmount = fixedAmount,
            MaximumAmount = maximumAmount,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }

    /// <summary>The version in force on a business date, or null when the option had no rule then (it is never guessed).</summary>
    public static GovernedServiceFeeOptionRate? Resolve(IEnumerable<GovernedServiceFeeOptionRate> versions, DateOnly businessDate) =>
        versions.Where(x => x.EffectiveDate <= businessDate)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedAtUtc).FirstOrDefault();

    /// <summary>Validates one transaction amount against this version. Returns null when acceptable, else a reason code.</summary>
    public string? CheckAmount(decimal amount)
    {
        if (amount <= 0m || amount > Collection.MaximumMoneyAmount
            || decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            return "AMOUNT_INVALID";
        return Basis switch
        {
            GovernedServiceBasis.FixedAmount => amount == FixedAmount ? null : "AMOUNT_NOT_APPROVED",
            GovernedServiceBasis.DirectApprovedAmount => MaximumAmount is { } ceiling && amount > ceiling ? "AMOUNT_ABOVE_CEILING" : null,
            _ => "AMOUNT_NOT_APPROVED"
        };
    }

    private static void ValidateMoney(decimal amount, string parameterName)
    {
        if (amount <= 0m || amount > Collection.MaximumMoneyAmount
            || decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            throw new ArgumentOutOfRangeException(parameterName, "An approved amount must be a positive cent amount.");
    }
}
