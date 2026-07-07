using System;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.DeclineAssessmentRequest
{
    /// <summary>Platform-operator decline of a pending assessment request, with an optional notice message.</summary>
    public record DeclineAssessmentRequestCommand(
        Guid Id,
        string? DecisionMessage) : IRequest<Result<AssessmentRequestDto>>;
}
