using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.DailyCollections.GetSettleableNpmDays;

public class GetSettleableNpmDaysQueryHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    IStallRepository stallRepository,
    INpmMarketClosureRepository marketClosureRepository,
    IFeeRateResolver feeRateResolver) : IRequestHandler<GetSettleableNpmDaysQuery, Result<IReadOnlyList<SettleableNpmDayDto>>>
{
    public async Task<Result<IReadOnlyList<SettleableNpmDayDto>>> Handle(GetSettleableNpmDaysQuery request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<IReadOnlyList<SettleableNpmDayDto>>.NotFound();

        if (stall.Facility?.Code != FacilityCode.NPM)
            return Result<IReadOnlyList<SettleableNpmDayDto>>.Success(Array.Empty<SettleableNpmDayDto>());

        var today = PhilippineTime.Today;

        // Which lessee's days these are. Naming the term is what lets an ended occupancy's arrears be collected at
        // all; without it the stall's current contract answered, so a past lessee's month came back empty.
        // Whose days these are. A named term wins; otherwise the term that held the stall during the month being
        // viewed, because a past month belongs to the lessee of that month and not to whoever is in the stall today;
        // and failing both, the most recent term — what a current collection screen means by "this stall".
        var monthStart = new DateOnly(request.Year, request.Month, 1);
        var monthEnd = new DateOnly(request.Year, request.Month, DateTime.DaysInMonth(request.Year, request.Month));

        StallOccupancy? occupancy;
        if (request.ContractId is { } namedTerm && namedTerm != Guid.Empty)
        {
            occupancy = stall.ResolveOccupancy(namedTerm, today);
        }
        else
        {
            occupancy = stall.OccupanciesOverlapping(monthStart, monthEnd, today)
                    .OrderByDescending(o => o.Start)
                    .FirstOrDefault()
                ?? stall.ResolveOccupancy(null, today);
        }

        if (occupancy is null)
            return Result<IReadOnlyList<SettleableNpmDayDto>>.Success(Array.Empty<SettleableNpmDayDto>());

        var existing = (await dailyCollectionRepository.GetByStallAndMonthAsync(request.StallId, request.Year, request.Month, ct))
            .ToDictionary(dc => dc.CollectionDate);
        var closedDates = (await marketClosureRepository.GetByMonthAsync(request.Year, request.Month, ct))
            .Select(c => c.ClosureDate)
            .ToHashSet();

        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var result = new List<SettleableNpmDayDto>();

        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day > today) break;                                              // never bill future days
            if (day < occupancy.Start || day > occupancy.BillableEnd)
                continue;                                                        // not this lessee's to answer for
            if (closedDates.Contains(day))
                continue;                                                        // facility-wide closure — nothing owed
            existing.TryGetValue(day, out var dc);
            if (dc is not null && (dc.IsPaid || dc.IsAbsent))
                continue;                                                        // already collected or excused

            result.Add(new SettleableNpmDayDto(day, snapshot.Resolve(FeeRateKey.NpmDailyStall, day)));
        }

        return Result<IReadOnlyList<SettleableNpmDayDto>>.Success(result);
    }
}
