using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileBindInfo;

/// <summary>Resolves a collector-app bind token to its LGU + branding (anonymous, pre-login).</summary>
public record GetMobileBindInfoQuery(string Token) : IRequest<Result<MobileBindInfoDto>>;
