using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Audit;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Audit.GetAuditTrail;

public class GetAuditTrailQueryHandler(IAuditRepository auditRepository)
    : IRequestHandler<GetAuditTrailQuery, Result<AuditTrailDto>>
{
    public async Task<Result<AuditTrailDto>> Handle(GetAuditTrailQuery request, CancellationToken ct)
    {
        var result = await auditRepository.GetAuditTrailAsync(
            request.Search,
            request.Action,
            request.EntityType,
            request.Actor,
            request.FromUtc,
            request.ToUtc,
            request.Page,
            request.PageSize,
            request.IncludeOptions,
            ct);

        return Result<AuditTrailDto>.Success(result);
    }
}
