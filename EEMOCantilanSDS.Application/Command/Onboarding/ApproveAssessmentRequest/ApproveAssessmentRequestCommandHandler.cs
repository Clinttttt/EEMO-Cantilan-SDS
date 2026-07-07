using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Common.Security;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ApproveAssessmentRequest
{
    public class ApproveAssessmentRequestCommandHandler(IAppDbContext context, ICurrentUserService currentUser)
        : IRequestHandler<ApproveAssessmentRequestCommand, Result<AssessmentRequestDto>>
    {
        public async Task<Result<AssessmentRequestDto>> Handle(ApproveAssessmentRequestCommand request, CancellationToken ct)
        {
            if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
                return Result<AssessmentRequestDto>.Forbidden();

            var entity = await context.AssessmentRequests.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
            if (entity is null)
                return Result<AssessmentRequestDto>.NotFound();

            if (entity.Status != AssessmentRequestStatus.PendingReview)
                return Result<AssessmentRequestDto>.Failure("Only a pending request can be approved.");

            // Issue a secure onboarding link + create the LGU's staged draft.
            var token = SecureToken.NewUrlToken();
            var link = OnboardingLinks.Build(token);

            entity.Approve(link, request.DecisionMessage, currentUser.Username ?? "Operator");

            var draft = OnboardingDraft.Create(entity.Id, entity.Municipality, entity.Province, token, DateTime.UtcNow.AddDays(30));
            context.OnboardingDrafts.Add(draft);

            await context.SaveChangesAsync(ct);

            return Result<AssessmentRequestDto>.Success(entity.ToDto());
        }
    }
}
