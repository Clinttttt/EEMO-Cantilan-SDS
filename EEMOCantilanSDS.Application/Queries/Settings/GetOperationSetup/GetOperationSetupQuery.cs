using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Settings.GetOperationSetup;

public sealed record GetOperationSetupQuery : IRequest<Result<OperationSetupOverviewDto>>;
