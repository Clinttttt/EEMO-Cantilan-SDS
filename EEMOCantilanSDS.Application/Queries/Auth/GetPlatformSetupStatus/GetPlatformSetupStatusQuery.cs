using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetPlatformSetupStatus
{
    /// <summary>Whether the platform console still needs its first dedicated operator account created.</summary>
    public record GetPlatformSetupStatusQuery() : IRequest<Result<PlatformSetupStatusDto>>;

    public record PlatformSetupStatusDto(bool IsSetupRequired);
}
