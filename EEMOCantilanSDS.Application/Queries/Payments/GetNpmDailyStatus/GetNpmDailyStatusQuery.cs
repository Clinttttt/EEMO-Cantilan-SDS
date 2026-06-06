using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetNpmDailyStatus;

public record GetNpmDailyStatusQuery(FacilityCode FacilityCode, int Year, int Month)
    : IRequest<Result<IReadOnlyList<NpmStallDailyStatusDto>>>;
