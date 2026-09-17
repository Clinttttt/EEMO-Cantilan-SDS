using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Onboarding;

/// <summary>One active assessment/onboarding pipeline per municipality and province.</summary>
public static class OnboardingPipelineGuard
{
    public const string ActivePipelineIndex = "UX_AssessmentRequests_ActiveMunicipality";

    public static IQueryable<AssessmentRequest> Active(IQueryable<AssessmentRequest> requests) =>
        requests.Where(r =>
            r.Status == AssessmentRequestStatus.PendingReview
            || (r.Status == AssessmentRequestStatus.Approved
                && (r.Stage == "Onboarding" || r.Stage == "Validation" || r.Stage == "Activation")));

    public static async Task<AssessmentRequest?> FindOtherActiveAsync(
        IAppDbContext context,
        string municipality,
        string province,
        Guid? exceptId,
        CancellationToken ct)
    {
        var normalizedMunicipality = Normalize(municipality);
        var normalizedProvince = Normalize(province);

        return await Active(context.AssessmentRequests)
            .Where(r => (!exceptId.HasValue || r.Id != exceptId.Value)
                && r.Municipality.Trim().ToUpper() == normalizedMunicipality
                && r.Province.Trim().ToUpper() == normalizedProvince)
            .OrderByDescending(r => r.Stage == "Activation")
            .ThenByDescending(r => r.Stage == "Validation")
            .ThenByDescending(r => r.Stage == "Onboarding")
            .ThenByDescending(r => r.UpdatedAt ?? r.SubmittedAt)
            .FirstOrDefaultAsync(ct);
    }

    public static string DuplicateMessage(AssessmentRequest existing) =>
        $"{existing.Municipality} already has an active onboarding request in {DisplayStage(existing)}.";

    public static bool IsActivePipelineConstraintViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var constraint = current.GetType().GetProperty("ConstraintName")?.GetValue(current) as string;
            if (string.Equals(constraint, ActivePipelineIndex, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static string Normalize(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string DisplayStage(AssessmentRequest request) =>
        request.Status == AssessmentRequestStatus.PendingReview ? "Pending Review" : request.Stage;
}
