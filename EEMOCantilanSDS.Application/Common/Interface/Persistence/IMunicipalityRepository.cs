using EEMOCantilanSDS.Domain.Entities.Tenancy;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IMunicipalityRepository
{
    /// <summary>All municipalities in the registry (default LGU first, then alphabetical).</summary>
    Task<IReadOnlyList<Municipality>> GetAllAsync(CancellationToken ct);

    /// <summary>The default LGU (Cantilan today), or null if the registry is empty. Used to source
    /// portal branding (municipality name, province) from the record rather than static constants.</summary>
    Task<Municipality?> GetDefaultAsync(CancellationToken ct);

    /// <summary>Resolves a single LGU by its subdomain identifier — its <c>TenantCode</c> (case-insensitive)
    /// or its <c>Code</c>. Used for public pre-login branding. Null when no match.</summary>
    Task<Municipality?> GetByIdentifierAsync(string identifier, CancellationToken ct);
}
