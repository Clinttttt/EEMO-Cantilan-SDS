using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;

public class SaveDailyCollectionOrNumberCommandHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SaveDailyCollectionOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveDailyCollectionOrNumberCommand request, CancellationToken ct)
    {
        var collection = await dailyCollectionRepository.GetByStallAndDateAsync(request.StallId, request.CollectionDate, ct);

        // Only an existing PAID day can be receipted; an unpaid/absent day has nothing to OR.
        if (collection is null || !collection.IsPaid)
            return Result<bool>.NotFound();

        collection.SetOrNumber(request.ORNumber.Trim(), currentUser.Username ?? "Admin");
        await unitOfWork.SaveChangesAsync(ct);   // GetByStallAndDateAsync returns a tracked entity
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            FacilityCode.NPM,
            collection.CollectionDate.Year,
            collection.CollectionDate.Month,
            ct);

        return Result<bool>.Success(true);
    }
}
