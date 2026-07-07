using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ApproveOnboardingValidation
{
    public class ApproveOnboardingValidationCommandHandler(IAppDbContext context, ICurrentUserService currentUser)
        : IRequestHandler<ApproveOnboardingValidationCommand, Result<AssessmentRequestDto>>
    {
        public async Task<Result<AssessmentRequestDto>> Handle(ApproveOnboardingValidationCommand request, CancellationToken ct)
        {
            if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
                return Result<AssessmentRequestDto>.Forbidden();

            var entity = await context.AssessmentRequests.FirstOrDefaultAsync(x => x.Id == request.AssessmentRequestId, ct);
            if (entity is null)
                return Result<AssessmentRequestDto>.NotFound();

            if (entity.Stage != "Validation")
                return Result<AssessmentRequestDto>.Failure("Only a request in validation can be approved for activation.");

            entity.ApproveValidation(currentUser.Username ?? "Operator");
            await context.SaveChangesAsync(ct);

            return Result<AssessmentRequestDto>.Success(entity.ToDto());
        }
    }
}
