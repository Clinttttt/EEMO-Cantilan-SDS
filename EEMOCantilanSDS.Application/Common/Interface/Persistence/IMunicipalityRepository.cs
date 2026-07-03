using EEMOCantilanSDS.Domain.Entities.Tenancy;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IMunicipalityRepository
{
    /// <summary>All municipalities in the registry (default LGU first, then alphabetical).</summary>
    Task<IReadOnlyList<Municipality>> GetAllAsync(CancellationToken ct);
}
