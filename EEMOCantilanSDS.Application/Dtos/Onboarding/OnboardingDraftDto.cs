using System;
using EEMOCantilanSDS.Domain.Entities.Onboarding;

namespace EEMOCantilanSDS.Application.Dtos.Onboarding;

/// <summary>
/// The staged onboarding draft returned to the LGU workspace (by token) and the operator (by request).
/// <c>ConfigJson</c> is the opaque configuration document owned by the workspace UI.
/// </summary>
public record OnboardingDraftDto(
    Guid Id,
    Guid AssessmentRequestId,
    string Municipality,
    string Province,
    string? ConfigJson,
    bool IsSubmittedForValidation,
    DateTime? SubmittedAt,
    DateTime ExpiresAt,
    bool IsExpired);

public static class OnboardingDraftMapping
{
    public static OnboardingDraftDto ToDto(this OnboardingDraft d) => new(
        d.Id,
        d.AssessmentRequestId,
        d.Municipality,
        d.Province,
        d.ConfigJson,
        d.IsSubmittedForValidation,
        d.SubmittedAt,
        d.ExpiresAt,
        d.IsExpired);
}
