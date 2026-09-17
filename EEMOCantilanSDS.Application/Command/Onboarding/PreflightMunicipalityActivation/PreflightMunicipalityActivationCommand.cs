using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.PreflightMunicipalityActivation;

public sealed record PreflightMunicipalityActivationCommand(ActivateMunicipalityCommand Activation)
    : IRequest<Result<ActivationPreflightResultDto>>;
public sealed record ActivationPreflightResultDto(bool CanActivate);

public sealed class PreflightMunicipalityActivationCommandHandler(IAppDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<PreflightMunicipalityActivationCommand, Result<ActivationPreflightResultDto>>
{
    public async Task<Result<ActivationPreflightResultDto>> Handle(PreflightMunicipalityActivationCommand request,
        CancellationToken cancellationToken)
    {
        var result = await MunicipalityActivationPreflight.CheckAsync(context, currentUser, request.Activation, cancellationToken);
        return result.IsSuccess
            ? Result<ActivationPreflightResultDto>.Success(new(true))
            : MunicipalityActivationPreflight.CopyFailure<ActivationPreflightResultDto>(result);
    }
}
