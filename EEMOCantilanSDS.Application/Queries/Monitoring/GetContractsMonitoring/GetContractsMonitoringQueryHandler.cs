using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Monitoring.GetContractsMonitoring;

public sealed class GetContractsMonitoringQueryHandler(IContractsMonitoringQueries queries, ICurrentUserService user,
    ICurrentMunicipalityAccessor municipality, IClock clock) : IRequestHandler<GetContractsMonitoringQuery, Result<ContractsMonitoringPageDto>>
{
    public async Task<Result<ContractsMonitoringPageDto>> Handle(GetContractsMonitoringQuery request, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is null || user.UserId == Guid.Empty) return Result<ContractsMonitoringPageDto>.Unauthorized();
        if (user.Role is not ("SuperAdmin" or "Admin") || municipality.MunicipalityId == Guid.Empty ||
            user.MunicipalityId != municipality.MunicipalityId) return Result<ContractsMonitoringPageDto>.Forbidden();
        var today = clock.PhilippineToday; // Capture once: page, summary and all statuses use the same local day.
        if (request.Filters.AsOf is { } requested && requested != today)
            return Result<ContractsMonitoringPageDto>.Failure("Contracts Monitoring supports Philippine today only; historical or future asOf dates are not supported.", ResultStatus.Invalid);
        return Result<ContractsMonitoringPageDto>.Success(await queries.GetAsync(request.Filters, today, ct));
    }
}
