using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// Stable tenant identity of one governed configurable service (IA-044). It carries no money rule of its own: the
/// amount basis, ceiling and enabled state live in append-only <see cref="GovernedServiceSetting"/> versions, and
/// the revenue classification and OR/CT instrument come from the tenant's revenue-classification policy. It is the
/// <c>SourceId</c> of the canonical lines the service produces.
/// </summary>
public sealed class GovernedService : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }

    /// <summary>The stable operation identity (<see cref="CollectorOperationCodes"/>), unique per tenant.</summary>
    public string OperationCode { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private GovernedService() { }

    public static GovernedService Create(Guid municipalityId, string operationCode, string createdBy, DateTime? createdAtUtc = null)
    {
        if (municipalityId == Guid.Empty)
            throw new ArgumentException("A tenant is required.", nameof(municipalityId));
        if (!CollectorOperationCodes.IsSupported(operationCode) || operationCode == CollectorOperationCodes.Wcf)
            throw new ArgumentException("The operation is not a governed configurable service.", nameof(operationCode));
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
            throw new ArgumentException("The configuring actor is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));

        return new GovernedService
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            OperationCode = operationCode,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }
}

/// <summary>
/// One immutable, effective-dated version of a governed service's approved setup. History is never edited: a change
/// appends a new version, and posted collection lines freeze the version they used.
/// </summary>
public sealed class GovernedServiceSetting : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid GovernedServiceId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public GovernedServiceBasis Basis { get; private set; }

    /// <summary>The approved amount when <see cref="Basis"/> is <see cref="GovernedServiceBasis.FixedAmount"/>.</summary>
    public decimal? FixedAmount { get; private set; }

    /// <summary>Optional approved ceiling per transaction when <see cref="Basis"/> is a direct approved amount.</summary>
    public decimal? MaximumAmount { get; private set; }

    /// <summary>False = Disabled for new transactions (history untouched).</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Whether the approved definition allows Collector Mobile to record it.</summary>
    public bool MobileEnabled { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private GovernedServiceSetting() { }

    public static GovernedServiceSetting Create(
        Guid municipalityId, Guid governedServiceId, DateOnly effectiveDate, GovernedServiceBasis basis,
        decimal? fixedAmount, decimal? maximumAmount, bool isEnabled, bool mobileEnabled, string createdBy,
        DateTime? createdAtUtc = null)
    {
        if (municipalityId == Guid.Empty || governedServiceId == Guid.Empty)
            throw new ArgumentException("A tenant and service are required.");
        if (!Enum.IsDefined(basis))
            throw new ArgumentOutOfRangeException(nameof(basis));
        switch (basis)
        {
            case GovernedServiceBasis.FixedAmount:
                ValidateMoney(fixedAmount ?? 0m, nameof(fixedAmount));
                if (maximumAmount is not null)
                    throw new ArgumentException("A fixed-amount service has no separate ceiling.", nameof(maximumAmount));
                break;
            case GovernedServiceBasis.VehicleClassRate:
            case GovernedServiceBasis.ApprovedFeeOption:
                if (fixedAmount is not null || maximumAmount is not null)
                    throw new ArgumentException("This service takes its amounts from its approved vehicle classes or fee options.", nameof(fixedAmount));
                break;
            case GovernedServiceBasis.DirectApprovedAmount:
                if (fixedAmount is not null)
                    throw new ArgumentException("A direct-approved-amount service has no fixed amount.", nameof(fixedAmount));
                if (maximumAmount is { } ceiling)
                    ValidateMoney(ceiling, nameof(maximumAmount));
                break;
        }
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
            throw new ArgumentException("The configuring actor is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));

        return new GovernedServiceSetting
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            GovernedServiceId = governedServiceId,
            EffectiveDate = effectiveDate,
            Basis = basis,
            FixedAmount = fixedAmount,
            MaximumAmount = maximumAmount,
            IsEnabled = isEnabled,
            MobileEnabled = mobileEnabled,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }

    /// <summary>
    /// The version in force on a business date: the latest effective on or before it, and among versions sharing an
    /// effective date the one recorded last. Null means the service was not set up on that date.
    /// </summary>
    public static GovernedServiceSetting? Resolve(IEnumerable<GovernedServiceSetting> versions, DateOnly businessDate) =>
        versions.Where(x => x.EffectiveDate <= businessDate)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

    /// <summary>Validates one transaction amount against this version. Returns null when acceptable, else a reason code.</summary>
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
