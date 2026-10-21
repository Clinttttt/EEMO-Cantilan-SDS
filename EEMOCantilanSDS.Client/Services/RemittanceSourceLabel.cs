using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>How a remittance row names its source. Display only: every line and amount still belongs to the collection.</summary>
public static class RemittanceSourceLabel
{
    /// <summary>Distinct source names in line order, so several lines of one parent read once ("Slaughterhouse", not "Slaughterhouse · Slaughterhouse").</summary>
    public static string For(RemittanceCollectionDto collection) => collection.Lines.Count == 0 ? "—"
        : string.Join(" · ", collection.Lines.Select(l => SourceNames.For(l.Name)).Distinct(StringComparer.OrdinalIgnoreCase));
}
