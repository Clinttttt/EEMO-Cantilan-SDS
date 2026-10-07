using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// A tenant-configurable vehicle class for Transportation / Parking (IA-030, IA-050): a stable identity independent of its
/// display name. Its price is not stored here but in append-only <see cref="VehicleClassRate"/> versions, and historical
/// trips never carried a class, so none is ever inferred for them.
/// </summary>
public sealed class VehicleClass : BaseEntity, IMunicipalityOwned
{
    public TerminalSection? TerminalSection { get; private set; }
    public void AssociateTerminalSection(TerminalSection section)
    {
        if (section is not (global::EEMOCantilanSDS.Domain.Entities.Revenue.TerminalSection.PullPulVansCargoVans or global::EEMOCantilanSDS.Domain.Entities.Revenue.TerminalSection.Tricycad))
            throw new ArgumentException("InvalidTerminalSection");
        var confirmedSection = Code switch
        {
            "TRICYCLE" => global::EEMOCantilanSDS.Domain.Entities.Revenue.TerminalSection.Tricycad,
            "JEEPNEY" or "MULTICAB" or "VAN" or "PUBLIC_UTILITY_BUS" or "PUBLIC_UTILITY_BABY_BUS" => global::EEMOCantilanSDS.Domain.Entities.Revenue.TerminalSection.PullPulVansCargoVans,
            _ => (TerminalSection?)null
        };
        if (confirmedSection.HasValue && confirmedSection != section)
            throw new ArgumentException("VehicleSectionConflict");
        TerminalSection = section;
    }
    public Guid MunicipalityId { get; private set; }

    /// <summary>The stable identity, unique per tenant (for example JEEPNEY).</summary>
    public string Code { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private VehicleClass() { }

    public static VehicleClass Create(Guid municipalityId, string code, string displayName, string createdBy, DateTime? createdAtUtc = null)
    {
        if (municipalityId == Guid.Empty)
            throw new ArgumentException("A tenant is required.", nameof(municipalityId));
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[A-Z][A-Z0-9_]{1,39}$"))
            throw new ArgumentException("The code must be 2-40 letters, digits or underscores, starting with a letter.", nameof(code));
        var name = (displayName ?? string.Empty).Trim();
        if (name.Length is 0 or > 80)
            throw new ArgumentException("A display name of 1-80 characters is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
            throw new ArgumentException("The configuring actor is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));
        return new VehicleClass
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            Code = normalized,
            DisplayName = name,
            IsActive = true,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }

    public void Rename(string displayName)
    {
        var name = (displayName ?? string.Empty).Trim();
        if (name.Length is 0 or > 80)
            throw new ArgumentException("A display name of 1-80 characters is required.", nameof(displayName));
        DisplayName = name;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}

/// <summary>One immutable, effective-dated approved rate of a vehicle class. A change appends a version; posted collections freeze the one they used.</summary>
public sealed class VehicleClassRate : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid VehicleClassId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private VehicleClassRate() { }

    public static VehicleClassRate Create(
        Guid municipalityId, Guid vehicleClassId, DateOnly effectiveDate, decimal amount, string createdBy, DateTime? createdAtUtc = null)
    {
        if (municipalityId == Guid.Empty || vehicleClassId == Guid.Empty)
            throw new ArgumentException("A tenant and a vehicle class are required.");
        if (amount <= 0m || amount > Collection.MaximumMoneyAmount || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("An approved rate must be a positive amount in whole centavos.", nameof(amount));
        if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Trim().Length > 100)
            throw new ArgumentException("The configuring actor is required.", nameof(createdBy));
        var created = createdAtUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The creation time must be UTC.", nameof(createdAtUtc));
        return new VehicleClassRate
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            VehicleClassId = vehicleClassId,
            EffectiveDate = effectiveDate,
            Amount = amount,
            CreatedAtUtc = created,
            CreatedBy = createdBy.Trim()
        };
    }

    /// <summary>The version in force on a business date, or null when the class had no rate then (it is never guessed).</summary>
    public static VehicleClassRate? Resolve(IEnumerable<VehicleClassRate> versions, DateOnly businessDate) =>
        versions.Where(x => x.EffectiveDate <= businessDate)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedAtUtc).FirstOrDefault();
}
