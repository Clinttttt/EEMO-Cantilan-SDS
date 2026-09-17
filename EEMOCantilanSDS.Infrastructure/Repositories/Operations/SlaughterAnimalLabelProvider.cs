using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public sealed class SlaughterAnimalLabelProvider(AppDbContext context) : ISlaughterAnimalLabelProvider
{
    public async Task<SlaughterAnimalLabels> GetAsync(CancellationToken ct = default)
    {
        var configured = await context.SlaughterAnimalLabels
            .AsNoTracking()
            .ToDictionaryAsync(x => x.AnimalType, x => x.DisplayLabel, ct);

        return new SlaughterAnimalLabels(
            configured.GetValueOrDefault(AnimalType.Hog, "Hog"),
            configured.GetValueOrDefault(AnimalType.Carabao, "Carabao"),
            configured.GetValueOrDefault(AnimalType.Cow, "Cow"));
    }

    public async Task<IReadOnlyList<string>> GetCustomNamesAsync(CancellationToken ct = default)
    {
        var municipalityId = context.CurrentMunicipalityId;
        if (municipalityId == Guid.Empty) return [];

        // Ordinary reads remain tenant-filtered and exclude retired names. PostgreSQL's unfiltered normalized index
        // independently preserves the registry's existing no-reuse behavior for direct/concurrent persistence writes.
        var configured = await context.SlaughterAnimalRates
            .AsNoTracking()
            .Where(x => x.MunicipalityId == municipalityId)
            .Select(x => x.AnimalName)
            .ToListAsync(ct);

        // Visible historical rows retain their original AnimalType/name identity. Include them when reserving a spelling
        // so a later label edit or differently-cased write cannot make current history ambiguous; no row is rewritten.
        var historical = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.MunicipalityId == municipalityId
                        && x.AnimalType == AnimalType.Other
                        && x.CustomAnimalType != null)
            .Select(x => x.CustomAnimalType!)
            .ToListAsync(ct);

        return configured.Concat(historical).Distinct(StringComparer.Ordinal).ToList();
    }
}
