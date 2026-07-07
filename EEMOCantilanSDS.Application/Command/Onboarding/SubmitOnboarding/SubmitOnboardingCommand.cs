using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SubmitOnboarding
{
    /// <summary>Submits the LGU's onboarding draft for operator validation (anonymous — by secure token).</summary>
    public record SubmitOnboardingCommand(string Token) : IRequest<Result<OnboardingDraftDto>>;
}
