using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Slaughterhouse;

public sealed record SlaughterAnimalLabels(string Hog, string Carabao, string Cow)
{
    public static SlaughterAnimalLabels Canonical { get; } = new("Hog", "Carabao", "Cow");

    public string For(AnimalType type) => type switch
    {
        AnimalType.Hog => Hog,
        AnimalType.Carabao => Carabao,
        AnimalType.Cow => Cow,
        _ => SlaughterAnimalNames.Canonical(type),
    };
}

public interface ISlaughterAnimalLabelProvider
{
    Task<SlaughterAnimalLabels> GetAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCustomNamesAsync(CancellationToken ct = default);
}
