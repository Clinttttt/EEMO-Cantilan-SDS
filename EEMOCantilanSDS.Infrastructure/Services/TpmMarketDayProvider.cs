using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Services;

/// <summary>
/// Reads the current tenant's Tabo-an market weekday from its Municipality record (resolved by the JWT
/// tenant claim), defaulting to Friday when unset so Cantilan/goldens stay unchanged.
/// </summary>
public class TpmMarketDayProvider(IAppDbContext context, ITenantContext tenantContext) : ITpmMarketDayProvider
{
    public async Task<DayOfWeek> GetMarketDayAsync(CancellationToken ct = default)
    {
        var code = tenantContext.TenantCode;
        var day = await context.Municipalities
            .IgnoreQueryFilters()
            .Where(m => m.TenantCode == code)
            .Select(m => m.TpmMarketDay)
            .FirstOrDefaultAsync(ct);
        return day ?? DayOfWeek.Friday;
    }
}
