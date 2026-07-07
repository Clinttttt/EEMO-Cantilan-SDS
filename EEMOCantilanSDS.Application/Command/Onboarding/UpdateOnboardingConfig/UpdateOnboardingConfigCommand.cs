using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.UpdateOnboardingConfig
{
    /// <summary>Saves the LGU's edited onboarding configuration (anonymous — by secure token).</summary>
    public record UpdateOnboardingConfigCommand(string Token, string? ConfigJson) : IRequest<Result<OnboardingDraftDto>>;

    /// <summary>Request body for the PUT endpoint — the token comes from the route, not the body.</summary>
    public record UpdateOnboardingConfigRequest(string? ConfigJson);
}
