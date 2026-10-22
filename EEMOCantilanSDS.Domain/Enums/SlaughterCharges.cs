namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>How a Slaughterhouse charge's quantity is set: per animal (the head count, never typed) or per unit the collector counts.</summary>
public enum FeeQuantityBasis
{
    PerHead = 1,
    PerUnit = 2
}

/// <summary>The animals an approved Slaughterhouse charge applies to. A charge with no scope applies to every animal.</summary>
[Flags]
public enum SlaughterAnimalScope
{
    None = 0,
    Hog = 1,
    Carabao = 2,
    Cow = 4,
    Other = 8,
    All = Hog | Carabao | Cow | Other
}

public static class SlaughterAnimalScopes
{
    public static SlaughterAnimalScope Of(AnimalType animal) => animal switch
    {
        AnimalType.Hog => SlaughterAnimalScope.Hog,
        AnimalType.Carabao => SlaughterAnimalScope.Carabao,
        AnimalType.Cow => SlaughterAnimalScope.Cow,
        _ => SlaughterAnimalScope.Other
    };

    /// <summary>True when the stored scope (null or empty = every animal) includes the animal.</summary>
    public static bool Includes(int? stored, AnimalType animal) =>
        stored is null or 0 || (stored.Value & (int)Of(animal)) != 0;
}
