using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
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

        var contract = stall.Contracts.FirstOrDefault(c => c.IsActive);
        if (contract is null)
            return Result<IReadOnlyList<SettleableNpmDayDto>>.Success(Array.Empty<SettleableNpmDayDto>());

        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(request.Year, request.Month, 1);
        var monthEnd = new DateOnly(request.Year, request.Month, DateTime.DaysInMonth(request.Year, request.Month));

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
            if (!(contract.EffectivityDate <= day && day <= contract.ExpiryDate))
                continue;                                                        // outside the contract term
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
