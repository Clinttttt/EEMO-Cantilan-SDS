using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
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

            draft.SubmitForValidation("LGU");

            // Advance the linked assessment request into the Validation stage.
            var requestEntity = await context.AssessmentRequests.FirstOrDefaultAsync(r => r.Id == draft.AssessmentRequestId, ct);
            requestEntity?.SubmitForValidation("LGU");

            await context.SaveChangesAsync(ct);

            return Result<OnboardingDraftDto>.Success(draft.ToDto());
        }
    }
}
