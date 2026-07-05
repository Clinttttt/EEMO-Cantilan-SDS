using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth;

public class GetDailyCollectionMonthQueryHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    IStallRepository stallRepository,
    IFeeRateResolver feeRateResolver,
    INpmMarketClosureRepository marketClosureRepository) : IRequestHandler<GetDailyCollectionMonthQuery, Result<DailyCollectionMonthDto>>
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
        // NPM market closures excuse EVERY payor for the day (facility-wide, no per-stall record), so
        // a closed day must read as "Closed" — never unpaid/missed — to match the Financial Reports.
        var closedDays = new HashSet<int>();
        if (stall.Facility?.Code == FacilityCode.NPM)
        {
            var closures = await marketClosureRepository.GetByMonthAsync(request.Year, request.Month, ct);
            foreach (var c in closures)
                if (c.ClosureDate.Day >= 1 && c.ClosureDate.Day <= daysInMonth)
                    closedDays.Add(c.ClosureDate.Day);
        }

        var daysCollected = 0;
        var daysCollectedPast = 0;   // only days up to today, for DaysMissed calculation
        var daysAbsentPast = 0;      // individually excused/absent days up to today — excluded from DaysMissed
        var daysAbsentAll = 0;       // individually excused/absent days in the whole month
        var daysClosedPast = 0;      // market-closed days up to today — excluded from DaysMissed
        var daysClosedAll = 0;       // market-closed days in the whole month
        var totalFishKilos = 0m;

        var collectionDict = new Dictionary<string, DailyCollectionDayDto>();

        foreach (var collection in collections)
        {
            var day = collection.CollectionDate.Day;
            var closedToday = closedDays.Contains(day);
            var key = collection.CollectionDate.ToString("yyyy-MM-dd");
            collectionDict[key] = new DailyCollectionDayDto(
                collection.CollectionDate,
                collection.IsPaid,
                collection.FishKilos,
                // A market closure outranks an individual "absent" marker for display.
                IsAbsent: collection.IsAbsent && !closedToday,
                IsMarketClosed: closedToday && !collection.IsPaid,
                ORNumber: collection.ORNumber
            );

            if (collection.IsPaid && day >= contractStartDay)
            {
                // Count ALL paid days — including future pre-paid days written by admin. A payor who
                // paid on a (later) closed day keeps the paid credit.
                daysCollected++;
                if (day <= maxDay)
                    daysCollectedPast++;
                if (collection.FishKilos.HasValue)
                    totalFishKilos += collection.FishKilos.Value;
            }
            else if (closedToday && day >= contractStartDay)
            {
                // Counted in the market-closure pass below (single source of truth for closed days).
            }
            else if (collection.IsAbsent && day >= contractStartDay)
            {
                // Excused/absent: nothing owed for the day — it leaves the missed/expected denominator.
                daysAbsentAll++;
                if (day <= maxDay)
                    daysAbsentPast++;
            }
        }

        // Fold in market-closed days (those not already paid) as a distinct excused "Closed" state.
        foreach (var day in closedDays)
        {
            if (day < contractStartDay || day > daysInMonth)
                continue;
            var date = new DateOnly(request.Year, request.Month, day);
            var key = date.ToString("yyyy-MM-dd");
            if (collectionDict.TryGetValue(key, out var existing) && existing.IsPaid)
                continue;   // paid on a closed day stays paid
            if (!collectionDict.ContainsKey(key))
                collectionDict[key] = new DailyCollectionDayDto(date, false, null, IsAbsent: false, IsMarketClosed: true);
            daysClosedAll++;
            if (day <= maxDay)
                daysClosedPast++;
        }

        var daysMissed    = Math.Max(0, validDays - daysCollectedPast - daysAbsentPast - daysClosedPast);
        // Resolve the municipality's NPM rates as of the report month (falls back to the ordinance
        // constants, so Cantilan totals are unchanged).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = new DateOnly(request.Year, request.Month, 1);
        var npmDaily = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, asOf);
        var fishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, asOf);
        var totalDailyFee = daysCollected * npmDaily;
        var totalFishFee  = totalFishKilos * fishRate;
        var grandTotal    = totalDailyFee + totalFishFee;
        // Fully settled when every collectable day from contract start is collected, excused, or closed.
        var fullMonthDays = Math.Max(0, daysInMonth - contractStartDay + 1);
        var isFullyPaid   = fullMonthDays > 0 && (daysCollected + daysAbsentAll + daysClosedAll) >= fullMonthDays;

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
            daysAbsentAll,
            daysClosedAll
        ));
    }
}
