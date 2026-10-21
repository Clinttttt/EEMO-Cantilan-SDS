using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SubmitOnboarding
{
    public class SubmitOnboardingCommandHandler(IAppDbContext context)
        : IRequestHandler<SubmitOnboardingCommand, Result<OnboardingDraftDto>>
    {
        public async Task<Result<OnboardingDraftDto>> Handle(SubmitOnboardingCommand request, CancellationToken ct)
        {
            var draft = await context.OnboardingDrafts.FirstOrDefaultAsync(x => x.Token == request.Token, ct);
            if (draft is null)
                return Result<OnboardingDraftDto>.NotFound();

            if (draft.IsExpired)
                return Result<OnboardingDraftDto>.Failure("This onboarding link has expired. Please contact the platform team.");

            if (string.IsNullOrWhiteSpace(draft.ConfigJson))
                return Result<OnboardingDraftDto>.Failure("Please complete your configuration before submitting for validation.");

            // A draft saved by an older workspace carries no declarations and passes. A malformed block is refused here too,
            // so a document that never went through the save validator cannot reach the operator.
            var operationErrors = OnboardingOperationDeclarations.Validate(draft.ConfigJson);
            if (operationErrors.Count > 0)
                return Result<OnboardingDraftDto>.Failure(string.Join(" ", operationErrors));

            var requestEntity = await context.AssessmentRequests
                .FirstOrDefaultAsync(r => r.Id == draft.AssessmentRequestId, ct);
            if (requestEntity is null
                || requestEntity.Status != AssessmentRequestStatus.Approved
                || requestEntity.Stage != "Onboarding")
                return Result<OnboardingDraftDto>.Failure(
                    "This onboarding request is no longer open for submission.", ResultStatus.Conflict);

            var existing = await OnboardingPipelineGuard.FindOtherActiveAsync(
                context, draft.Municipality, draft.Province, draft.AssessmentRequestId, ct);
            if (existing is not null)
                return Result<OnboardingDraftDto>.Failure(
                    OnboardingPipelineGuard.DuplicateMessage(existing), ResultStatus.Conflict);

            draft.SubmitForValidation("LGU");

            // Advance the linked assessment request into the Validation stage.
            requestEntity.SubmitForValidation("LGU");

            await context.SaveChangesAsync(ct);

            return Result<OnboardingDraftDto>.Success(draft.ToDto());
        }
    }
}
