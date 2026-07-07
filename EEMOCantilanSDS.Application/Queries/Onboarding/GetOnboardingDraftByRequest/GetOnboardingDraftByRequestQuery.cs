using System;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Onboarding.GetOnboardingDraftByRequest
{
    /// <summary>Operator (platform-operator only) read of an LGU's onboarding draft by its assessment request id.</summary>
    public record GetOnboardingDraftByRequestQuery(Guid AssessmentRequestId) : IRequest<Result<OnboardingDraftDto>>;
}
