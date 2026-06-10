using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileTpmCollection;

public sealed class GetMobileTpmCollectionQueryHandler(
    ICollectorRepository collectorRepository,
    ITpmRepository tpmRepository,
    ISuggestionRepository suggestionRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetMobileTpmCollectionQuery, Result<MobileTpmCollectionDto>>
{
    public async Task<Result<MobileTpmCollectionDto>> Handle(GetMobileTpmCollectionQuery request, CancellationToken ct)
    {
        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileTpmCollectionDto>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, ct);
        if (collector is null)
            return Result<MobileTpmCollectionDto>.NotFound();

        if (!collector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.TPM))
            return Result<MobileTpmCollectionDto>.Forbidden();

        var today = PhilippineTime.Today;
        var isMarketDay = today.DayOfWeek == DayOfWeek.Friday;
        var marketDate = MostRecentFriday(today);

        var attendances = await tpmRepository.GetVendorAttendanceAsync(marketDate, ct);

        // Distinct goods across all registered vendors — feeds the mobile "Goods" picker.
        var allVendors = await tpmRepository.GetAllVendorsAsync(ct);
        var knownGoods = allVendors
            .Select(v => v.Goods)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Drop any goods the office has hidden (blocklisted) from the picker.
        var hiddenGoods = await suggestionRepository.GetHiddenValuesAsync(SuggestionType.TpmGoods, ct);
        knownGoods = knownGoods.Where(g => !hiddenGoods.Contains(g)).ToList();

        return Result<MobileTpmCollectionDto>.Success(new MobileTpmCollectionDto(
            marketDate,
            isMarketDay,
            FeeRates.TpmVendorFee,
            attendances.Count,
            attendances.Where(a => a.IsPaid).Sum(a => a.Fee),
            attendances,
            knownGoods));
    }

    // Today if it is a Friday, otherwise the most recent past Friday.
    private static DateOnly MostRecentFriday(DateOnly today)
    {
        int diff = ((int)today.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
        return today.AddDays(-diff);
    }
}
