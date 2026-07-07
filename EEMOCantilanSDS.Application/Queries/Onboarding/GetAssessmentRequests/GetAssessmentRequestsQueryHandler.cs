using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Onboarding.GetAssessmentRequests
{
    public class GetAssessmentRequestsQueryHandler(IAppDbContext context, ICurrentUserService currentUser)
        : IRequestHandler<GetAssessmentRequestsQuery, Result<IReadOnlyList<AssessmentRequestDto>>>
    {
        public async Task<Result<IReadOnlyList<AssessmentRequestDto>>> Handle(GetAssessmentRequestsQuery request, CancellationToken ct)
        {
            if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
                return Result<IReadOnlyList<AssessmentRequestDto>>.Forbidden();

            var requests = await context.AssessmentRequests
                .AsNoTracking()
                .OrderByDescending(x => x.SubmittedAt)
                .ToListAsync(ct);

            var dtos = requests.Select(r => r.ToDto()).ToList();
            return Result<IReadOnlyList<AssessmentRequestDto>>.Success(dtos);
        }
    }
}
