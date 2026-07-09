using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ISetupRepository
{
    /// <summary>True when the DEFAULT (Cantilan) LGU already has a Head/SuperAdmin — first-run setup is
    /// scoped to the default LGU so activating other LGUs (which create their own Heads) never blocks it.</summary>
    Task<bool> IsSuperAdminExistsAsync(CancellationToken ct);

    /// <summary>The default (Cantilan) municipality id, or empty if the registry isn't seeded yet.</summary>
    Task<Guid> GetDefaultMunicipalityIdAsync(CancellationToken ct);

    Task AddFirstAdminAsync(AdminUser admin, CancellationToken ct);
}
