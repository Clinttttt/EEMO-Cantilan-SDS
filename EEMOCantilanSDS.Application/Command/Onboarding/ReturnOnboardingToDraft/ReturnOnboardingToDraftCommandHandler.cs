using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ReturnOnboardingToDraft
{
    public class ReturnOnboardingToDraftCommandHandler(IAppDbContext context, ICurrentUserService currentUser)
        : IRequestHandler<ReturnOnboardingToDraftCommand, Result<AssessmentRequestDto>>
    {
        public async Task<Result<AssessmentRequestDto>> Handle(ReturnOnboardingToDraftCommand request, CancellationToken ct)
        {
            if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
                return Result<AssessmentRequestDto>.Forbidden();

            var entity = await context.AssessmentRequests.FirstOrDefaultAsync(x => x.Id == request.AssessmentRequestId, ct);
            if (entity is null)
                return Result<AssessmentRequestDto>.NotFound();

            if (entity.Status != AssessmentRequestStatus.Approved
                || entity.Stage != "Validation")
                return Result<AssessmentRequestDto>.Failure(
                    "Only a request in validation can be returned to onboarding.");

            var existing = await OnboardingPipelineGuard.FindOtherActiveAsync(
                context, entity.Municipality, entity.Province, entity.Id, ct);
            if (existing is not null)
                return Result<AssessmentRequestDto>.Failure(
                    OnboardingPipelineGuard.DuplicateMessage(existing), ResultStatus.Conflict);

            var by = currentUser.Username ?? "Operator";
            entity.ReturnToOnboarding(by);

            var draft = await context.OnboardingDrafts.FirstOrDefaultAsync(d => d.AssessmentRequestId == request.AssessmentRequestId, ct);
            draft?.Reopen(by);

            await context.SaveChangesAsync(ct);

            return Result<AssessmentRequestDto>.Success(entity.ToDto());
        }
    }
}
