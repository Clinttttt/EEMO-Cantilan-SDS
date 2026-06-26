using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth;

public class GetDailyCollectionMonthQueryHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    IStallRepository stallRepository) : IRequestHandler<GetDailyCollectionMonthQuery, Result<DailyCollectionMonthDto>>
{
    public async Task<Result<DailyCollectionMonthDto>> Handle(GetDailyCollectionMonthQuery request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<DailyCollectionMonthDto>.NotFound();

        var collections = await dailyCollectionRepository.GetByStallAndMonthAsync(request.StallId, request.Year, request.Month, ct);

        var daysInMonth = DateTime.DaysInMonth(request.Year, request.Month);
        var today = PhilippineTime.Today;
        var isCurrentMonth = request.Year == today.Year && request.Month == today.Month;
        var maxDay = isCurrentMonth ? today.Day : daysInMonth;

        // Get contract start date to determine valid collection days
        var contractStartDay = 1;
        var activeContract = stall.Contracts.FirstOrDefault(c => c.IsActive);
        if (activeContract != null)
        {
            var contractDate = activeContract.EffectivityDate;
            if (contractDate.Year == request.Year && contractDate.Month == request.Month)
            {
                contractStartDay = contractDate.Day;
            }
            else if (contractDate.Year > request.Year || (contractDate.Year == request.Year && contractDate.Month > request.Month))
            {
                // Contract hasn't started yet in this month
                contractStartDay = maxDay + 1;
            }
        }

        var validDays = Math.Max(0, maxDay - contractStartDay + 1);
        var daysCollected = 0;
        var daysCollectedPast = 0;   // only days up to today, for DaysMissed calculation
        var daysAbsentPast = 0;      // excused/absent days up to today — excluded from DaysMissed
        var daysAbsentAll = 0;       // excused/absent days in the whole month (display + fully-paid)
        var totalFishKilos = 0m;

        var collectionDict = new Dictionary<string, DailyCollectionDayDto>();

        foreach (var collection in collections)
        {
            var key = collection.CollectionDate.ToString("yyyy-MM-dd");
            collectionDict[key] = new DailyCollectionDayDto(
                collection.CollectionDate,
                collection.IsPaid,
                collection.FishKilos,
                collection.IsAbsent
            );

            if (collection.IsPaid && collection.CollectionDate.Day >= contractStartDay)
            {
                // Count ALL paid days — including future pre-paid days written by admin
                daysCollected++;

                // Separately track only past-or-today days for DaysMissed
                if (collection.CollectionDate.Day <= maxDay)
                    daysCollectedPast++;

                if (collection.FishKilos.HasValue)
                    totalFishKilos += collection.FishKilos.Value;
            }
            else if (collection.IsAbsent && collection.CollectionDate.Day >= contractStartDay)
            {
                // Excused/absent: nothing owed for the day — it leaves the missed/expected denominator.
                daysAbsentAll++;
                if (collection.CollectionDate.Day <= maxDay)
                    daysAbsentPast++;
            }
        }

        var daysMissed    = Math.Max(0, validDays - daysCollectedPast - daysAbsentPast);
        var totalDailyFee = daysCollected * FeeRates.NpmDailyFee;
        var totalFishFee  = totalFishKilos * FeeRates.NpmFishFeePerKilo;
        var grandTotal    = totalDailyFee + totalFishFee;
        // Fully settled when every collectable day from contract start is either collected or excused.
        var fullMonthDays = Math.Max(0, daysInMonth - contractStartDay + 1);
        var isFullyPaid   = fullMonthDays > 0 && (daysCollected + daysAbsentAll) >= fullMonthDays;

        return Result<DailyCollectionMonthDto>.Success(new DailyCollectionMonthDto(
            request.Year,
            request.Month,
            validDays,
            daysCollected,
            daysMissed,
            totalDailyFee,
            totalFishKilos,
            totalFishFee,
            grandTotal,
            isFullyPaid,
            collectionDict,
            daysAbsentAll
        ));
    }
}
