using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Slaughterhouse;

/// <summary>Maps canonical slaughter animal identities to their shared per-head ordinance rates.</summary>
public static class SlaughterRateKeys
{
    public static FeeRateKey? For(AnimalType animalType) => animalType switch
    {
        AnimalType.Hog => FeeRateKey.SlhHogPerHead,
        AnimalType.Carabao or AnimalType.Cow => FeeRateKey.SlhLargePerHead,
        _ => null
    };
}
