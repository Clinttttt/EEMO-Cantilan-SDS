using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;

public sealed class CreateRevenueClassificationCommandHandler(
    IAppDbContext context,
    ICurrentUserService currentUser)
    : IRequestHandler<CreateRevenueClassificationCommand, Result<RevenueClassificationDto>>
{
    public async Task<Result<RevenueClassificationDto>> Handle(
        CreateRevenueClassificationCommand request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var municipalityId, out var actor))
            return Result<RevenueClassificationDto>.Forbidden();

        var duplicate = await context.RevenueClassifications
            .AsNoTracking()
            .AnyAsync(x => x.MunicipalityId == municipalityId && x.SemanticCode == request.SemanticCode, cancellationToken);
        if (duplicate)
            return Result<RevenueClassificationDto>.Failure(
                "A revenue classification with that semantic identity already exists in this municipality.",
                ResultStatus.Conflict);

        // Domain factories own the identity and field invariants. MunicipalityId and actor are taken only from
        // the authenticated request context; neither is accepted from the client.
        var classification = RevenueClassification.Create(request.SemanticCode, municipalityId, actor);
        var policy = RevenueClassificationPolicy.Create(
            classification.Id,
            request.EffectiveDate,
            request.DisplayName,
            request.PermittedInstrumentType,
            municipalityId,
            request.Description,
            actor);

        context.RevenueClassifications.Add(classification);
        context.RevenueClassificationPolicies.Add(policy);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (RevenueClassificationPersistenceConflicts.IsDuplicateSemanticCode(ex))
        {
            // The pre-check provides the normal response; the tenant-scoped database index closes a concurrent
            // create race and is translated to the same explicit conflict.
            return Result<RevenueClassificationDto>.Failure(
                "A revenue classification with that semantic identity already exists in this municipality.",
                ResultStatus.Conflict);
        }

        var effectivePolicy = new RevenueClassificationPolicyDto(
            policy.Id, policy.EffectiveDate, policy.DisplayName, policy.Description,
            policy.PermittedInstrumentType, policy.CreatedAt, policy.CreatedBy);

        return Result<RevenueClassificationDto>.Success(new RevenueClassificationDto(
            classification.Id, classification.SemanticCode, classification.IsActive, true, effectivePolicy));
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
