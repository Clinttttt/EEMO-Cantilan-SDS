using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Bounded browse/search of explicit business identities. Text never establishes a source link.</summary>
public static class BusinessPayorSearch
{
    public const int MaximumRows = 50;

    public static IQueryable<Payor> Apply(IQueryable<Payor> source, Guid tenantId, string? search)
    {
        var term = search?.Trim().ToLowerInvariant() ?? string.Empty;
        return source.Where(x => x.MunicipalityId == tenantId && !x.IsDeleted
                && (term == "" || x.DisplayName.ToLower().Contains(term)))
            .OrderBy(x => x.DisplayName.ToLower()).ThenBy(x => x.Id).Take(MaximumRows);
    }
}
