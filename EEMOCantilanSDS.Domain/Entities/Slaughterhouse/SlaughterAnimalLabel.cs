using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Slaughterhouse;

/// <summary>
/// Tenant wording for one stable built-in slaughterhouse animal identity. Financial rates and historical
/// transactions remain keyed by <see cref="AnimalType"/>/<see cref="FeeRateKey"/> and never by this text.
/// </summary>
public sealed class SlaughterAnimalLabel : AuditableEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public AnimalType AnimalType { get; private set; }
    public string DisplayLabel { get; private set; } = string.Empty;

    private SlaughterAnimalLabel() { }

    public static SlaughterAnimalLabel Create(
        AnimalType animalType,
        string displayLabel,
        Guid municipalityId = default,
        string createdBy = "System")
    {
        if (!SlaughterAnimalNames.IsBuiltIn(animalType))
            throw new ArgumentOutOfRangeException(nameof(animalType), "Only Hog, Carabao, and Cow have canonical labels.");
        if (string.IsNullOrWhiteSpace(displayLabel))
            throw new ArgumentException("Display label is required.", nameof(displayLabel));

        return new SlaughterAnimalLabel
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            AnimalType = animalType,
            DisplayLabel = displayLabel.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };
    }

    public void Rename(string displayLabel, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(displayLabel))
            throw new ArgumentException("Display label is required.", nameof(displayLabel));
        DisplayLabel = displayLabel.Trim();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
