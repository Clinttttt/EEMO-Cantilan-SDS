using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Onboarding.GetOnboardingDraftByRequest
{
    public class GetOnboardingDraftByRequestQueryHandler(IAppDbContext context, ICurrentUserService currentUser)
        : IRequestHandler<GetOnboardingDraftByRequestQuery, Result<OnboardingDraftDto>>
    {
        public async Task<Result<OnboardingDraftDto>> Handle(GetOnboardingDraftByRequestQuery request, CancellationToken ct)
        {
            if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
                return Result<OnboardingDraftDto>.Forbidden();

            var draft = await context.OnboardingDrafts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.AssessmentRequestId == request.AssessmentRequestId, ct);

            if (draft is null)
                return Result<OnboardingDraftDto>.NotFound();

            return Result<OnboardingDraftDto>.Success(draft.ToDto());
        }
    }
}
