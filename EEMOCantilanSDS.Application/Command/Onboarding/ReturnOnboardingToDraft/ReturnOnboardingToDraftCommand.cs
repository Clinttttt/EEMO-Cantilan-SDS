using System;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ReturnOnboardingToDraft
{
    /// <summary>Platform-operator returns a submitted config for corrections — reopens the draft (Onboarding).</summary>
    public record ReturnOnboardingToDraftCommand(Guid AssessmentRequestId, string? Note) : IRequest<Result<AssessmentRequestDto>>;
}
