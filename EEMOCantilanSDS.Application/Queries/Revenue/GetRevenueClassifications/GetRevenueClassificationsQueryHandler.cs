using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassifications;

public sealed class GetRevenueClassificationsQueryHandler(
    IAppDbContext context,
    ICurrentUserService currentUser,
    IClock clock)
    : IRequestHandler<GetRevenueClassificationsQuery, Result<IReadOnlyList<RevenueClassificationDto>>>
{
    public async Task<Result<IReadOnlyList<RevenueClassificationDto>>> Handle(
        GetRevenueClassificationsQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.MunicipalityId is not { } municipalityId
            || municipalityId == Guid.Empty)
            return Result<IReadOnlyList<RevenueClassificationDto>>.Forbidden();

        var asOf = request.AsOf ?? clock.PhilippineToday;
        var classifications = await context.RevenueClassifications
            .AsNoTracking()
            .Where(x => x.MunicipalityId == municipalityId)
            .OrderBy(x => x.SemanticCode)
            .ThenBy(x => x.Id)
            .Select(x => new ClassificationRow(x.Id, x.SemanticCode, x.IsActive))
            .ToListAsync(cancellationToken);

        if (classifications.Count == 0)
            return Result<IReadOnlyList<RevenueClassificationDto>>.Success(Array.Empty<RevenueClassificationDto>());

        var ids = classifications.Select(x => x.Id).ToArray();
        var policies = await context.RevenueClassificationPolicies
            .AsNoTracking()
            .Where(x => x.MunicipalityId == municipalityId && ids.Contains(x.RevenueClassificationId))
            .ToListAsync(cancellationToken);

        var byClassification = policies
            .GroupBy(x => x.RevenueClassificationId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var result = classifications.Select(classification =>
        {
            var versions = byClassification.GetValueOrDefault(classification.Id) ?? [];
            var effective = versions
                .Where(x => x.EffectiveDate <= asOf)
                .OrderByDescending(x => x.EffectiveDate)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();

            return new RevenueClassificationDto(
                classification.Id,
                classification.SemanticCode,
                classification.IsActive,
                versions.Count != 0,
                effective is null ? null : Map(effective));
        }).ToList();

        return Result<IReadOnlyList<RevenueClassificationDto>>.Success(result);
    }

    private static RevenueClassificationPolicyDto Map(RevenueClassificationPolicy policy) =>
        new(policy.Id, policy.EffectiveDate, policy.DisplayName, policy.Description,
            policy.PermittedInstrumentType, policy.CreatedAt, policy.CreatedBy);

    private sealed record ClassificationRow(Guid Id, string SemanticCode, bool IsActive);
}
