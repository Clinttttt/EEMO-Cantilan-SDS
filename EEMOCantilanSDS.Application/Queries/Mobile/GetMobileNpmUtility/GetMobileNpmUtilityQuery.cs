using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmUtility;

/// <summary>The month's NPM utility bills for the mobile collector to settle.</summary>
public record GetMobileNpmUtilityQuery(int Year, int Month) : IRequest<Result<MobileNpmUtilityDto>>;
