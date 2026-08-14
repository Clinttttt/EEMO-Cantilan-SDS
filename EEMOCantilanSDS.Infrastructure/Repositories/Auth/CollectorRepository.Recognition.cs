using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EEMOCantilanSDS.Infrastructure.Repositories;
// Partial of CollectorRepository: the private arithmetic every projection above shares — which payments count as recognized
// revenue for a date range, how a prepaid daily amount is allocated across collectable days, which days are collectable at
// all, and the small labelling helpers.
//
// Held in one file on purpose. These decide what a peso is counted AS and WHEN, and the office reconciles the collector's
// app against its own reports by hand: two copies of this arithmetic is two answers to the same question.
public partial class CollectorRepository
{
    private static string PeriodOccupant(IEnumerable<Contract> contracts, DateOnly periodStart, DateOnly periodEnd)
        => contracts.Where(c => c.EffectivityDate <= periodEnd && periodStart <= c.ExpiryDate)
                    .OrderByDescending(c => c.EffectivityDate)
                    .Select(c => c.ActualOccupant)
                    .FirstOrDefault()
           ?? contracts.OrderByDescending(c => c.EffectivityDate).Select(c => c.ActualOccupant).FirstOrDefault()
           ?? "—";


    private static bool IsDailyReportFacility(FacilityCode facility) =>
        facility is FacilityCode.NPM or FacilityCode.SLH or FacilityCode.TRM or FacilityCode.TPM;

    private static bool IsMonthlyRentalFacility(FacilityCode facility) =>
        facility is FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE
            or FacilityCode.Custom1 or FacilityCode.Custom2 or FacilityCode.Custom3 or FacilityCode.Custom4 or FacilityCode.Custom5;

    private static bool IsPaymentInDateRange(int billingYear, int billingMonth, DateOnly startDate, DateOnly endDate)
    {
        var billingDate = new DateOnly(billingYear, billingMonth, 1);
        var rangeStart = new DateOnly(startDate.Year, startDate.Month, 1);
        var rangeEnd = new DateOnly(endDate.Year, endDate.Month, 1);

        return billingDate >= rangeStart && billingDate <= rangeEnd;
    }

    private static bool IsWholeBillingMonthSelected(PaymentRecord payment, DateOnly startDate, DateOnly endDate)
    {
        var monthStart = new DateOnly(payment.BillingYear, payment.BillingMonth, 1);
        var monthEnd = new DateOnly(payment.BillingYear, payment.BillingMonth, DateTime.DaysInMonth(payment.BillingYear, payment.BillingMonth));
        return startDate <= monthStart && endDate >= monthEnd;
    }

    private decimal RecognizedNpmPaymentRevenue(PaymentRecord payment, DateOnly startDate, DateOnly endDate, Stall stall)
    {
        if (payment.Status == PaymentStatus.Unpaid || !IsPaymentInDateRange(payment.BillingYear, payment.BillingMonth, startDate, endDate))
            return 0m;

        var dailyRevenue = RecognizedNpmDailyFeeRevenue(payment, startDate, endDate, stall);
        if (!IsWholeBillingMonthSelected(payment, startDate, endDate) || payment.Status != PaymentStatus.Paid)
            return dailyRevenue;

        return dailyRevenue + payment.FishKilos.GetValueOrDefault() * _npmFishRate;
    }

    private decimal RecognizedNpmDailyFeeRevenue(PaymentRecord payment, DateOnly startDate, DateOnly endDate, Stall stall)
    {
        if (payment.Status == PaymentStatus.Unpaid || !IsPaymentInDateRange(payment.BillingYear, payment.BillingMonth, startDate, endDate))
            return 0m;

        var monthStart = new DateOnly(payment.BillingYear, payment.BillingMonth, 1);
        var monthEnd = new DateOnly(payment.BillingYear, payment.BillingMonth, DateTime.DaysInMonth(payment.BillingYear, payment.BillingMonth));
        var overlapStart = startDate > monthStart ? startDate : monthStart;
        var overlapEnd = endDate < monthEnd ? endDate : monthEnd;

        if (overlapEnd < overlapStart || CountNpmCollectableDays(stall, overlapStart, overlapEnd) == 0)
            return 0m;

        var paidTowardDailyFee = payment.Status == PaymentStatus.Paid
            ? payment.BaseRentalAmount
            : Math.Min(payment.PartialAmount, payment.BaseRentalAmount);

        return AllocatePrepaidDailyAmountToCollectableRange(paidTowardDailyFee, stall, monthStart, overlapStart, overlapEnd);
    }

    private bool NpmPaymentCoversDate(PaymentRecord payment, DateOnly date, Stall stall) =>
        RecognizedNpmDailyFeeRevenue(payment, date, date, stall) > 0m;

    private decimal AllocatePrepaidDailyAmountToCollectableRange(
        decimal prepaidAmount,
        Stall stall,
        DateOnly monthStart,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        // A custom-section stall's prepaid daily amount is divided by ITS daily rate; canonical uses ordinance.
        var dailyRate = stall.ResolveDailyFee(_npmDailyRate);
        if (prepaidAmount <= 0m || dailyRate <= 0m || rangeEnd < rangeStart)
            return 0m;

        var monthEnd = new DateOnly(monthStart.Year, monthStart.Month, DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
        var collectableDays = new List<DateOnly>();
        for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
        {
            if (IsStallCollectableOn(stall, date))
                collectableDays.Add(date);
        }

        var fullCoveredDays = (int)Math.Floor(prepaidAmount / dailyRate);
        var remainder = prepaidAmount % dailyRate;
        var amount = collectableDays
            .Take(fullCoveredDays)
            .Where(d => d >= rangeStart && d <= rangeEnd)
            .Sum(_ => dailyRate);

        if (remainder > 0m && collectableDays.Count > fullCoveredDays)
        {
            var remainderDay = collectableDays[fullCoveredDays];
            if (remainderDay >= rangeStart && remainderDay <= rangeEnd)
                amount += remainder;
        }

        return amount;
    }

    private static bool IsContractCollectableOn(Contract contract, DateOnly date) =>
        contract.IsCollectableOn(date);

    private static bool IsStallCollectableOn(Stall stall, DateOnly date) =>
        stall.Status == StallStatus.Active
        && stall.Contracts.Any(c => IsContractCollectableOn(c, date));

    private static int CountNpmCollectableDays(Stall stall, DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            return 0;

        var days = 0;
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (IsStallCollectableOn(stall, date))
                days++;
        }

        return days;
    }

    private static DateOnly ClampDate(DateOnly value, DateOnly min, DateOnly max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }

    private static string FacilityName(FacilityCode code, IReadOnlyDictionary<FacilityCode, string> names) =>
        names.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name) ? name : code.ToString();

    private static string ReportPayeeKey(FacilityCode facility, string? stallNo, string payorName) =>
        $"{facility}|{stallNo?.Trim().ToUpperInvariant()}|{payorName.Trim().ToUpperInvariant()}";

    private static int CountCollectableDays(DateOnly? contractStart, DateOnly monthStart, DateOnly effectiveEnd)
    {
        if (effectiveEnd < monthStart)
            return 0;

        var start = contractStart.HasValue && contractStart.Value > monthStart
            ? contractStart.Value
            : monthStart;

        return start > effectiveEnd ? 0 : effectiveEnd.DayNumber - start.DayNumber + 1;
    }

    private static string GetAreaLabel(Domain.Entities.Facilities.Stall stall)
    {
        if (stall.AreaLocation.HasValue)
            return stall.AreaLocation.Value.ToString();

        return stall.Section.HasValue ? GetSectionName(stall.Section) : (stall.CustomSectionName ?? string.Empty);
    }

    // Resolves a stall's section name for display: a canonical MarketSection uses the canonical name; a
    // custom NPM section (Section null) uses its per-stall CustomSectionName.
    private static string SectionDisplayName(Stall s) =>
        s.Section.HasValue ? GetSectionName(s.Section) : (s.CustomSectionName ?? string.Empty);

    private static string GetSectionName(MarketSection? section) => section switch
    {
        MarketSection.VegetableArea => "Vegetables",
        MarketSection.FishSection => "Fish",
        MarketSection.MeatSection => "Meat",
        _ => string.Empty
    };

    private sealed record CollectorReportTransaction(
        FacilityCode FacilityCode,
        string FacilityName,
        string? StallNo,
        string PayorName,
        DateOnly PeriodDate,
        decimal Amount,
        bool IsPartial,
        DateTime CollectedAt,
        string? ORNumber,
        bool IsAdminRecorded);
}
