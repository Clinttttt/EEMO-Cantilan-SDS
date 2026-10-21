using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Monitoring.GetContractsMonitoring;

public sealed record GetContractsMonitoringQuery(ContractsMonitoringRequest Filters) : IRequest<Result<ContractsMonitoringPageDto>>;
