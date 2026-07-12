using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;

public class SaveDailyCollectionOrForDaysCommandHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SaveDailyCollectionOrForDaysCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveDailyCollectionOrForDaysCommand request, CancellationToken ct)
    {
        var or = (request.ORNumber ?? string.Empty).Trim();
        if (or.Length == 0)
            return Result<bool>.Failure("OR number is required.", 400);

        var dates = request.Dates.Distinct().OrderBy(d => d).ToList();
        if (dates.Count == 0)
            return Result<bool>.Failure("Select at least one day.", 400);

        var updatedBy = currentUser.Username ?? "Admin";
        var monthsTouched = new HashSet<(int Year, int Month)>();
        var applied = 0;

        foreach (var date in dates)
        {
            var collection = await dailyCollectionRepository.GetByStallAndDateAsync(request.StallId, date, ct);
            // Only an existing PAID day can be receipted; skip anything unpaid/absent/missing.
            if (collection is null || !collection.IsPaid)
                continue;
            collection.SetOrNumber(or, updatedBy);
            monthsTouched.Add((date.Year, date.Month));
            applied++;
        }

        if (applied == 0)
            return Result<bool>.Failure("None of the selected days could be receipted.", 400);

        await unitOfWork.SaveChangesAsync(ct);   // GetByStallAndDateAsync returns tracked entities

        foreach (var (year, month) in monthsTouched)
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(tenantContext.TenantCode, FacilityCode.NPM, year, month, ct);

        return Result<bool>.Success(true);
    }
}
