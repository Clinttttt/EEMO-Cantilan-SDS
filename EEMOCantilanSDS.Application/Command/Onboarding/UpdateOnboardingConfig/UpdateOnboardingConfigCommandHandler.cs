using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.UpdateOnboardingConfig
{
    public class UpdateOnboardingConfigCommandHandler(IAppDbContext context)
        : IRequestHandler<UpdateOnboardingConfigCommand, Result<OnboardingDraftDto>>
    {
        public async Task<Result<OnboardingDraftDto>> Handle(UpdateOnboardingConfigCommand request, CancellationToken ct)
        {
            var draft = await context.OnboardingDrafts.FirstOrDefaultAsync(x => x.Token == request.Token, ct);
            if (draft is null)
                return Result<OnboardingDraftDto>.NotFound();

            if (draft.IsExpired)
                return Result<OnboardingDraftDto>.Failure("This onboarding link has expired. Please contact the platform team.");

            draft.UpdateConfig(request.ConfigJson, "LGU");
            await context.SaveChangesAsync(ct);

            return Result<OnboardingDraftDto>.Success(draft.ToDto());
        }
    }
}
