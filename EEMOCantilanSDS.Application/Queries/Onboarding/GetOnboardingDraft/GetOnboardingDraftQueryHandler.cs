using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Onboarding.GetOnboardingDraft
{
    public class GetOnboardingDraftQueryHandler(IAppDbContext context)
        : IRequestHandler<GetOnboardingDraftQuery, Result<OnboardingDraftDto>>
    {
        public async Task<Result<OnboardingDraftDto>> Handle(GetOnboardingDraftQuery request, CancellationToken ct)
        {
            var draft = await context.OnboardingDrafts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Token == request.Token, ct);

            if (draft is null)
                return Result<OnboardingDraftDto>.NotFound();

            if (draft.IsExpired)
                return Result<OnboardingDraftDto>.Failure("This onboarding link has expired. Please contact the platform team.");

            return Result<OnboardingDraftDto>.Success(draft.ToDto());
        }
    }
}
