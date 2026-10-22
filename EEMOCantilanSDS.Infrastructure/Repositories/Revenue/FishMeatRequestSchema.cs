using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

public sealed class FishMeatRequestSchema(AppDbContext db) : IFishMeatRequestSchema
{
    private bool? _ready;
    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        if (_ready.HasValue) return _ready.Value;
        try
        {
            // Preserve the existing migration identity, including on normally deployed databases.
            if (!(await db.Database.GetAppliedMigrationsAsync(ct)).Contains("20261020133853_AddFishMeatRegistrationRequestsWorkingCopy20261020"))
                return (_ready = false).Value;
            _ = await db.FishMeatRegistrationRequests.AsNoTracking().Take(1).ToListAsync(ct);
            return (_ready = true).Value;
        }
        catch (PostgresException e) when (e.SqlState is "42P01" or "42703" or "42501")
        { return (_ready = false).Value; }
    }
}
