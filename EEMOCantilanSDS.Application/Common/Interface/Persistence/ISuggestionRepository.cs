using EEMOCantilanSDS.Domain.Entities.Suggestions;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ISuggestionRepository
{
    /// <summary>Hidden (blocklisted) values for a pick-list type, case-insensitive.</summary>
    Task<IReadOnlySet<string>> GetHiddenValuesAsync(SuggestionType type, CancellationToken ct = default);

    Task AddAsync(HiddenSuggestion suggestion, CancellationToken ct = default);
}
