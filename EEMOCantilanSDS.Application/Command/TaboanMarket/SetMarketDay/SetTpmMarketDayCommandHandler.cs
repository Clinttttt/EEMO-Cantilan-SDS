using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.SetMarketDay
{
    /// <summary>
    /// Records the office's new weekly market day, from the date it names.
    ///
    /// <para>
    /// Two rows are written the first time an office moves its day: one for the day it has been holding the market
    /// on, effective from before any of its records, and one for the new day. Without the first, every date before
    /// the change would fall back to the registry record — which now holds the NEW day — and the office's already
    /// collected weeks would read as having been held on a day that was not a market day.
    /// </para>
    /// </summary>
    public class SetTpmMarketDayCommandHandler(
        IAppDbContext context,
        ITpmMarketDayProvider marketDayProvider,
        IEemoCacheInvalidator cacheInvalidator,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        IClock clock) : IRequestHandler<SetTpmMarketDayCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(SetTpmMarketDayCommand request, CancellationToken ct)
        {
            var today = clock.PhilippineToday;

            if (request.EffectiveFrom < today)
                return Result<bool>.Failure(
                    "A market day cannot start in the past. The weeks already collected were held on the day in "
                    + "force then, and moving that day now would restate them.");

            if (request.EffectiveFrom.DayOfWeek != request.Day)
                return Result<bool>.Failure(
                    $"{request.EffectiveFrom:MMMM d, yyyy} is a {request.EffectiveFrom.DayOfWeek}, not a "
                    + $"{request.Day}. Choose the first {request.Day} the market will be held on.");

            var currentDay = await marketDayProvider.GetMarketDayAsync(today, ct);
            var dayOnThatDate = await marketDayProvider.GetMarketDayAsync(request.EffectiveFrom, ct);
            if (request.Day == dayOnThatDate)
                return Result<bool>.Failure($"The market is already held on a {request.Day} from that date.");

            var changedBy = currentUser.Username ?? "Head";

            // The office's history of its own arrangement, so a date before this change still resolves to the day
            // the market was actually held on.
            var hasHistory = await context.TpmMarketDaySchedules.AnyAsync(ct);
            if (!hasHistory)
            {
                context.TpmMarketDaySchedules.Add(TpmMarketDaySchedule.Create(
                    currentDay, DateOnly.MinValue, createdBy: changedBy));
            }

            var existing = await context.TpmMarketDaySchedules
                .FirstOrDefaultAsync(s => s.EffectiveFrom == request.EffectiveFrom, ct);

            if (existing is not null)
                return Result<bool>.Failure(
                    $"A market day is already recorded from {request.EffectiveFrom:MMMM d, yyyy}. Choose another date.");

            context.TpmMarketDaySchedules.Add(TpmMarketDaySchedule.Create(
                request.Day, request.EffectiveFrom, createdBy: changedBy));

            // The registry record carries the day the office holds the market on NOW, which is what its settings
            // and its onboarding profile state. Only moved once the new day has actually started.
            if (request.EffectiveFrom <= today)
            {
                // The registry is keyed by tenant code and is not itself tenant-filtered, so no filter has to be
                // switched off to reach this office's own row — and the code comes from the caller's own context.
                var municipality = await context.Municipalities
                    .FirstOrDefaultAsync(m => m.TenantCode == tenantContext.TenantCode, ct);

                municipality?.SetTpmMarketDay(request.Day, changedBy);
            }

            await context.SaveChangesAsync(ct);

            // The weekly market's own views name its market days, and a moved day changes which dates those are.
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                tenantContext.TenantCode, FacilityCode.TPM, request.EffectiveFrom.Year, request.EffectiveFrom.Month, ct);

            return Result<bool>.Success(true);
        }
    }
}
