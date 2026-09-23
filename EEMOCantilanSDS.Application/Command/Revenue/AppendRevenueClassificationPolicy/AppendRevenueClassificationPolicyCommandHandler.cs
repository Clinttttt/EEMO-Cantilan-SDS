using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Revenue.AppendRevenueClassificationPolicy;

public sealed class AppendRevenueClassificationPolicyCommandHandler(
    IAppDbContext context,
    ICurrentUserService currentUser)
    : IRequestHandler<AppendRevenueClassificationPolicyCommand, Result<RevenueClassificationPolicyDto>>
{
    public async Task<Result<RevenueClassificationPolicyDto>> Handle(
        AppendRevenueClassificationPolicyCommand request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var municipalityId, out var actor))
            return Result<RevenueClassificationPolicyDto>.Forbidden();

        var classificationExists = await context.RevenueClassifications
            .AsNoTracking()
            .AnyAsync(x => x.Id == request.ClassificationId && x.MunicipalityId == municipalityId, cancellationToken);
        if (!classificationExists)
            return Result<RevenueClassificationPolicyDto>.NotFound();

        var duplicateDate = await context.RevenueClassificationPolicies
            .AsNoTracking()
            .AnyAsync(x => x.MunicipalityId == municipalityId
                && x.RevenueClassificationId == request.ClassificationId
                && x.EffectiveDate == request.EffectiveDate, cancellationToken);
        if (duplicateDate)
            return Result<RevenueClassificationPolicyDto>.Failure(
                "A policy version already exists for this classification and effective date.",
                ResultStatus.Conflict);

        var policy = RevenueClassificationPolicy.Create(
            request.ClassificationId,
            request.EffectiveDate,
            request.DisplayName,
            request.PermittedInstrumentType,
            municipalityId,
            request.Description,
            actor);

        context.RevenueClassificationPolicies.Add(policy);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (RevenueClassificationPersistenceConflicts.IsDuplicatePolicyEffectiveDate(ex))
        {
            return Result<RevenueClassificationPolicyDto>.Failure(
                "A policy version already exists for this classification and effective date.",
                ResultStatus.Conflict);
        }

        return Result<RevenueClassificationPolicyDto>.Success(new RevenueClassificationPolicyDto(
            policy.Id, policy.EffectiveDate, policy.DisplayName, policy.Description,
            policy.PermittedInstrumentType, policy.CreatedAt, policy.CreatedBy));
    }

    private bool TryGetActor(out Guid municipalityId, out string actor)
    {
        municipalityId = currentUser.MunicipalityId.GetValueOrDefault();
        actor = currentUser.Username ?? string.Empty;
        return currentUser.IsAuthenticated
            && municipalityId != Guid.Empty
            && !string.IsNullOrWhiteSpace(actor)
            && actor.Length <= 100;
    }
}
