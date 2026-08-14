using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetCollectableDays;

public class GetCollectableDaysQueryHandler(
    IStallRepository stallRepo,
    IDailyCollectionRepository dailyRepo,
    INpmMarketClosureRepository closureRepo, IClock clock)
    : IRequestHandler<GetCollectableDaysQuery, Result<CollectableDaysDto>>
{
    public async Task<Result<CollectableDaysDto>> Handle(GetCollectableDaysQuery request, CancellationToken ct)
    {
        if (request.Month is < 1 or > 12 || request.Year is < 1990 or > 2200)
            return Result<CollectableDaysDto>.Failure("That is not a real month.", 400);

        var stall = await stallRepo.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<CollectableDaysDto>.NotFound();

        var today = clock.PhilippineToday;
        var daysInMonth = DateTime.DaysInMonth(request.Year, request.Month);

        var closures = new HashSet<DateOnly>(
            (await closureRepo.GetByMonthAsync(request.Year, request.Month, ct)).Select(c => c.ClosureDate));

        var onRecord = new Dictionary<DateOnly, DailyCollection>();
        foreach (var dc in await dailyRepo.GetByStallAndMonthAsync(request.StallId, request.Year, request.Month, ct))
            onRecord[dc.CollectionDate] = dc;

        // A day is owed when SOME term of this stall answers for it, not only the one in force - otherwise days left
        // behind by a lessee who has gone could never be collected.
        var occupancies = stall.Occupancies(today);

        var uncollected = new List<DateOnly>();
        var chargeable = new List<DateOnly>();
        var collected = 0;
        var excused = 0;
        var notOwed = 0;

        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(request.Year, request.Month, day);

            // A day that has not happened is not owed yet. Counted as nothing rather than as "not owed": the month in
            // progress is normal, and calling its remaining days closures would misdescribe them.
            if (date > today) continue;

            if (closures.Contains(date) || !occupancies.Any(o => o.Start <= date && date <= o.BillableEnd))
            {
                notOwed++;
                continue;
            }

            if (onRecord.TryGetValue(date, out var existing))
            {
                if (existing.IsPaid) { collected++; chargeable.Add(date); continue; }

                // Excused: nothing is owed, so it is not one of the payor's days to count either.
                if (existing.IsAbsent) { excused++; continue; }
            }

            uncollected.Add(date);
            chargeable.Add(date);
        }

        return Result<CollectableDaysDto>.Success(new CollectableDaysDto(
            request.StallId, request.Year, request.Month, uncollected, collected, excused, notOwed, chargeable));
    }
}