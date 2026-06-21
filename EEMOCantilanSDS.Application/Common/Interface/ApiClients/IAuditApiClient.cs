using EEMOCantilanSDS.Application.Dtos.Audit;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IAuditApiClient
{
    Task<Result<AuditTrailDto>> GetAuditTrailAsync(
        string? search = null,
        string? action = null,
        string? entityType = null,
        string? actor = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int page = 1,
        int pageSize = 25,
        bool includeOptions = true);
}
