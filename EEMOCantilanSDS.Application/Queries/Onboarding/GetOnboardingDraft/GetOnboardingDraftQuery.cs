using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Onboarding.GetOnboardingDraft
{
    /// <summary>Loads an LGU's onboarding draft by its secure token (anonymous — the token is the credential).</summary>
    public record GetOnboardingDraftQuery(string Token) : IRequest<Result<OnboardingDraftDto>>;
}
