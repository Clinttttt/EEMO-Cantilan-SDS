using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassificationPolicyHistory;

public sealed class GetRevenueClassificationPolicyHistoryQueryHandler(
    IAppDbContext context,
    ICurrentUserService currentUser)
    : IRequestHandler<GetRevenueClassificationPolicyHistoryQuery, Result<IReadOnlyList<RevenueClassificationPolicyDto>>>
{
    public async Task<Result<IReadOnlyList<RevenueClassificationPolicyDto>>> Handle(
        GetRevenueClassificationPolicyHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.MunicipalityId is not { } municipalityId
            || municipalityId == Guid.Empty)
            return Result<IReadOnlyList<RevenueClassificationPolicyDto>>.Forbidden();

        var exists = await context.RevenueClassifications
            .AsNoTracking()
            .AnyAsync(x => x.Id == request.ClassificationId && x.MunicipalityId == municipalityId, cancellationToken);
        if (!exists)
            return Result<IReadOnlyList<RevenueClassificationPolicyDto>>.NotFound();

        var policies = await context.RevenueClassificationPolicies
            .AsNoTracking()
            .Where(x => x.MunicipalityId == municipalityId && x.RevenueClassificationId == request.ClassificationId)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Id)
            .Select(x => new RevenueClassificationPolicyDto(
                x.Id, x.EffectiveDate, x.DisplayName, x.Description, x.PermittedInstrumentType, x.CreatedAt, x.CreatedBy))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<RevenueClassificationPolicyDto>>.Success(policies);
    }
}
