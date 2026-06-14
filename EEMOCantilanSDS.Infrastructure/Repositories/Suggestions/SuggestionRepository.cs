using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Suggestions;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class SuggestionRepository(AppDbContext context) : ISuggestionRepository
{
    public async Task<IReadOnlySet<string>> GetHiddenValuesAsync(SuggestionType type, CancellationToken ct = default)
    {
        var values = await context.HiddenSuggestions
            .AsNoTracking()
            .Where(h => h.Type == type)
            .Select(h => h.Value)
            .ToListAsync(ct);

        return values.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task AddAsync(HiddenSuggestion suggestion, CancellationToken ct = default)
        => await context.HiddenSuggestions.AddAsync(suggestion, ct);
}
