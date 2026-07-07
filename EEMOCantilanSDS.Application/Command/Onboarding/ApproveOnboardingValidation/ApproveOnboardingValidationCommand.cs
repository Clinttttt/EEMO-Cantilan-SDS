using System;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ApproveOnboardingValidation
{
    /// <summary>Platform-operator approval of the validation dry-run — advances the request to Activation.</summary>
    public record ApproveOnboardingValidationCommand(Guid AssessmentRequestId) : IRequest<Result<AssessmentRequestDto>>;
}
