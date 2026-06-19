using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorProfile;

/// <summary>The authenticated collector's own profile (resolved from the token, never the request).</summary>
public sealed record GetCollectorProfileQuery : IRequest<Result<MobileCollectorProfileDto>>;
