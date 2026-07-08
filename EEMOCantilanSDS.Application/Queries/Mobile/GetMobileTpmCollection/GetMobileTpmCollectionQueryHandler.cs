using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileTpmCollection;

public sealed class GetMobileTpmCollectionQueryHandler(
    ICollectorRepository collectorRepository,
    ITpmRepository tpmRepository,
    ISuggestionRepository suggestionRepository,
    IFeeRateResolver feeRateResolver,
    ITpmMarketDayProvider marketDayProvider,
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

        var marketDay = await marketDayProvider.GetMarketDayAsync(ct);
        var today = PhilippineTime.Today;
        var isMarketDay = today.DayOfWeek == marketDay;
        var marketDate = MostRecentMarketDay(today, marketDay);

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

        // Existing vendors (name + their usual goods) — feeds the mobile "Vendor name" picker so a
        // returning vendor can be selected (reused by name) instead of retyped, prefilling their goods.
        // Office-hidden vendor names are dropped from the suggestions.
        var hiddenVendors = await suggestionRepository.GetHiddenValuesAsync(SuggestionType.TpmVendor, ct);
        var knownVendors = allVendors
            .Where(v => !string.IsNullOrWhiteSpace(v.VendorName))
            .GroupBy(v => v.VendorName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => !hiddenVendors.Contains(g.Key))
            .Select(g => new MobileTpmKnownVendorDto(g.Key, g.First().Goods?.Trim() ?? string.Empty))
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The per-vendor fee shown to the collector is this municipality's current rate (constant fallback).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var vendorFee = rateSnapshot.Resolve(FeeRateKey.TpmVendorDay, marketDate);

        return Result<MobileTpmCollectionDto>.Success(new MobileTpmCollectionDto(
            marketDate,
            isMarketDay,
            vendorFee,
            attendances.Count,
            attendances.Where(a => a.IsPaid).Sum(a => a.Fee),
            attendances,
            knownGoods,
            knownVendors));
    }

    // Today if it is the market day, otherwise the most recent past market day.
    private static DateOnly MostRecentMarketDay(DateOnly today, DayOfWeek marketDay)
    {
        int diff = ((int)today.DayOfWeek - (int)marketDay + 7) % 7;
        return today.AddDays(-diff);
    }
}
