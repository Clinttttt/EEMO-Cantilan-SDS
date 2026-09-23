using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Revenue.RetireRevenueClassification;

public sealed class RetireRevenueClassificationCommandHandler(
    IAppDbContext context,
    ICurrentUserService currentUser)
    : IRequestHandler<RetireRevenueClassificationCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RetireRevenueClassificationCommand request, CancellationToken cancellationToken)
    {
        var actor = currentUser.Username;
        if (!currentUser.IsAuthenticated
            || currentUser.MunicipalityId is not { } municipalityId
            || municipalityId == Guid.Empty
            || string.IsNullOrWhiteSpace(actor)
            || actor.Length > 100)
            return Result<bool>.Forbidden();

        var classification = await context.RevenueClassifications
            .FirstOrDefaultAsync(x => x.Id == request.ClassificationId && x.MunicipalityId == municipalityId, cancellationToken);
        if (classification is null)
            return Result<bool>.NotFound();

        // Retirement preserves the semantic identity and every policy version. It is not soft or hard deletion.
        classification.Retire(actor);
        await context.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
