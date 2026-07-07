using System;
using EEMOCantilanSDS.Domain.Entities.Onboarding;

namespace EEMOCantilanSDS.Application.Dtos.Onboarding;

/// <summary>Projection of an <c>AssessmentRequest</c> for the operator console and public confirmation.</summary>
public record AssessmentRequestDto(
    Guid Id,
    string Municipality,
    string Province,
    string RequestingOffice,
    string FocalPerson,
    string Position,
    string OfficialEmail,
    string ContactNumber,
    string FacilitiesManaged,
    string? ApproxVendors,
    string? AuthorizationStatus,
    bool Acknowledged,
    string? Notes,
    string Status,
    string Stage,
    string? DecisionMessage,
    string? OnboardingLink,
    DateTime SubmittedAt);

public static class AssessmentRequestMapping
{
    public static AssessmentRequestDto ToDto(this AssessmentRequest r) => new(
        r.Id,
        r.Municipality,
        r.Province,
        r.RequestingOffice,
        r.FocalPerson,
        r.Position,
        r.OfficialEmail,
        r.ContactNumber,
        r.FacilitiesManaged,
        r.ApproxVendors,
        r.AuthorizationStatus,
        r.Acknowledged,
        r.Notes,
        r.Status.ToString(),
        r.Stage,
        r.DecisionMessage,
        r.OnboardingLink,
        r.SubmittedAt);
}
