using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.UpdateOnboardingConfig
{
    /// <summary>Saves the LGU's edited onboarding configuration (anonymous — by secure token).</summary>
    public record UpdateOnboardingConfigCommand(string Token, string? ConfigJson) : IRequest<Result<OnboardingDraftDto>>;
}
