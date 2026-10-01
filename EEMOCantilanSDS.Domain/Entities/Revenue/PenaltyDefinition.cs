using System.Text.RegularExpressions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// One immutable, effective-dated version of an approved penalty (IA-049): "Late payment", "Illegal vending", and so
/// on, as the office defines them. A fine reaches an Official Receipt only through a definition; a collector or
/// clerk never types a free-text charge with an arbitrary amount. History is append-only: a change adds a version,
/// and a posted collection line freezes the exact version it applied (its <see cref="Id"/> is the line's source id).
/// </summary>
public sealed class PenaltyDefinition : BaseEntity, IMunicipalityOwned
{
    private static readonly Regex ValidCode = new("^[A-Z][A-Z0-9_]{1,39}$", RegexOptions.Compiled);

    public Guid MunicipalityId { get; private set; }

    /// <summary>Stable tenant-defined identity shared by every version of one penalty.</summary>
    public string Code { get; private set; } = string.Empty;

    public DateOnly EffectiveDate { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Descriptive context only (for example "Stall rent arrears"). It changes no price and no eligibility.</summary>
    public string? AppliesTo { get; private set; }

    public GovernedServiceBasis Basis { get; private set; }
    public decimal? FixedAmount { get; private set; }
    public decimal? MaximumAmount { get; private set; }

    /// <summary>False = retired for new fines (history untouched).</summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private PenaltyDefinition() { }

    public static PenaltyDefinition Create(
        Guid municipalityId, string code, DateOnly effectiveDate, string displayName, string? appliesTo,
        GovernedServiceBasis basis, decimal? fixedAmount, decimal? maximumAmount, bool isActive, string createdBy,
        DateTime? createdAtUtc = null)
    {
        if (municipalityId == Guid.Empty)
            throw new ArgumentException("A tenant is required.", nameof(municipalityId));
        if (string.IsNullOrWhiteSpace(code) || !ValidCode.IsMatch(code))
            throw new ArgumentException("The penalty code must be 2-40 uppercase letters, digits or underscores and start with a letter.", nameof(code));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 120)
            throw new ArgumentException("A display name of at most 120 characters is required.", nameof(displayName));
        if (appliesTo?.Trim().Length > 80)
            throw new ArgumentException("The applies-to note must not exceed 80 characters.", nameof(appliesTo));
        if (!Enum.IsDefined(basis))
            throw new ArgumentOutOfRangeException(nameof(basis));
        switch (basis)
        {
            case GovernedServiceBasis.FixedAmount:
                ValidateMoney(fixedAmount ?? 0m, nameof(fixedAmount));
                if (maximumAmount is not null)
                    throw new ArgumentException("A fixed penalty has no separate ceiling.", nameof(maximumAmount));
                break;
            case GovernedServiceBasis.DirectApprovedAmount:
                if (fixedAmount is not null)
                    throw new ArgumentException("A manually approved penalty has no fixed amount.", nameof(fixedAmount));
                if (maximumAmount is { } ceiling)
                    ValidateMoney(ceiling, nameof(maximumAmount));
                break;
        }
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
            throw new ArgumentException("The defining actor is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));

        return new PenaltyDefinition
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            Code = code,
            EffectiveDate = effectiveDate,
            DisplayName = displayName.Trim(),
            AppliesTo = string.IsNullOrWhiteSpace(appliesTo) ? null : appliesTo.Trim(),
            Basis = basis,
            FixedAmount = fixedAmount,
            MaximumAmount = maximumAmount,
            IsActive = isActive,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }

    /// <summary>The version of one penalty in force on a date: latest effective on or before it, last recorded on ties.</summary>
    public static PenaltyDefinition? Resolve(IEnumerable<PenaltyDefinition> versions, string code, DateOnly businessDate) =>
        versions.Where(x => x.Code == code && x.EffectiveDate <= businessDate)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

    /// <summary>Null when the amount is acceptable for this version; otherwise a stable reason code.</summary>
    public string? CheckAmount(decimal amount)
    {
        if (amount <= 0m || amount > Collection.MaximumMoneyAmount
            || decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            return "AMOUNT_INVALID";
        return Basis switch
        {
            GovernedServiceBasis.FixedAmount => amount == FixedAmount ? null : "AMOUNT_NOT_APPROVED",
            GovernedServiceBasis.DirectApprovedAmount => MaximumAmount is { } ceiling && amount > ceiling
                ? "AMOUNT_ABOVE_CEILING" : null,
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
