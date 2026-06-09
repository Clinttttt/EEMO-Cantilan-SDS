using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;

public sealed record GetMobileMonthlyCollectionQuery(FacilityCode Facility, int Year, int Month)
    : IRequest<Result<MobileMonthlyCollectionDto>>;
