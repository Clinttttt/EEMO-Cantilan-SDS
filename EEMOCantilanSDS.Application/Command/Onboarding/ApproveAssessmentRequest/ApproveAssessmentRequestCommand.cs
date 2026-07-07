using System;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ApproveAssessmentRequest
{
    /// <summary>
    /// Platform-operator approval of a pending assessment request: records the onboarding link issued to the
    /// LGU and advances the request to the Onboarding stage. No live LGU data is written.
    /// </summary>
    public record ApproveAssessmentRequestCommand(
        Guid Id,
        string OnboardingLink,
        string? DecisionMessage) : IRequest<Result<AssessmentRequestDto>>;
}
