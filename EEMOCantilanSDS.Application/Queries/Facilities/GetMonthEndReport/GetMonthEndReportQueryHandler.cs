using System.Globalization;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetMonthEndReport;

/// <summary>
/// Assembles the all-facilities month-end report. Rental facilities (NPM/TCC/NCC/BBQ/ICE) come from the
/// canonical per-facility report aggregation (summary + per-payor compliance that reconciles to the
/// facility subtotal). Transaction facilities (SLH/TRM/TPM) come from their own month records, grouped
/// by payor (owner / driver / vendor) so repeats collapse into one expandable row whose total reconciles
/// to the facility subtotal. Grand totals are summed from the per-facility figures so the report
/// reconciles end to end. No new financial logic is introduced — only reuse, grouping, and mapping.
/// </summary>
public class GetMonthEndReportQueryHandler(
    IFacilityReportsRepository reportsRepository,
    ISlaughterRepository slaughterRepository,
    ITrmRepository trmRepository,
    ITpmRepository tpmRepository,
    IFeeRateResolver feeRateResolver,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions
) : IRequestHandler<GetMonthEndReportQuery, Result<MonthEndReportDto>>
{
    private static readonly FacilityCode[] RentalFacilities =
        { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE };

    public async Task<Result<MonthEndReportDto>> Handle(GetMonthEndReportQuery request, CancellationToken ct)
    {
        var key = EemoCacheKeys.MonthEndReport(tenantContext.TenantCode, request.Year, request.Month);
        var regions = EemoCacheRegions.MonthEndReportRegions(tenantContext.TenantCode, request.Year, request.Month);
        var report = await cache.GetOrCreateAsync(
            key,
            regions,
            cacheOptions.MonthEndReportTtl,
            token => BuildReportAsync(request, token),
            ct);

        return Result<MonthEndReportDto>.Success(report);
    }

    private async Task<MonthEndReportDto> BuildReportAsync(GetMonthEndReportQuery request, CancellationToken ct)
    {
        var facilities = new List<MonthEndFacilityDto>();

        // Resolve the municipality's NPM rates as of the report month (falls back to the ordinance
        // constants, so Cantilan figures are unchanged). Full-month reference = daily × 30.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = new DateOnly(request.Year, request.Month, 1);
        var npmDaily = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, asOf);
        var npmMonthly = npmDaily * 30m;

        // ── Rental facilities: per-payor compliance + summary from the canonical report aggregation ──
        foreach (var code in RentalFacilities)
        {
            var report = await reportsRepository.GetFacilityReportsAsync(
                code, ReportPeriod.Monthly, request.Year, request.Month, null, ct);

            // NPM is billed daily; surface the fixed full-month (30-day) coverage reference (₱900) and the
            // remaining balance toward it alongside the existing daily-based figures. Additive only — every
            // other facility keeps these at 0 and is unaffected.
            var isNpm = code == FacilityCode.NPM;

            var payors = report.StallCompliance
                .Select(s =>
                {
                    // Excused/absent days are not owed, so they lower the full-month (₱900) reference:
                    // coverage = ₱900 − (absent days × ₱30). A fully-absent month references ₱0.
                    var coverage = isNpm
                        ? Math.Max(0m, npmMonthly - s.AbsentDays * npmDaily)
                        : 0m;
                    return new MonthEndPayorDto(
                        s.StallNo, s.Occupant, s.MonthlyRate, s.Status, s.AmountPaid, s.Balance, s.ORNumber, s.DailyRate,
                        MonthlyCoverage: coverage,
                        MonthlyCoverageBalance: isNpm ? Math.Max(0m, coverage - s.AmountPaid) : 0m);
                })
                .ToList();

            facilities.Add(new MonthEndFacilityDto(
                Code: code,
                Name: FacilityName(code),
                IsRental: true,
                Collected: report.TotalRevenue,
                Outstanding: report.PendingPaymentAmount,
                CollectionRate: (int)Math.Round(report.CollectionRate),
                PaidCount: report.CollectionPerformance.FullyPaidCount,
                PartialCount: report.CollectionPerformance.PartiallyPaidCount,
                UnpaidCount: report.CollectionPerformance.UnpaidCount,
                TotalPayors: payors.Count,
                Payors: payors,
                TransactionPayors: Array.Empty<MonthEndTxnPayorDto>()));
        }

        // ── Transaction facilities: itemised month records grouped by payor ──
        var slaughter = await slaughterRepository.GetTransactionsByMonthAsync(request.Year, request.Month, ct);
        facilities.Add(BuildSlaughterFacility(slaughter));

        var trips = await trmRepository.GetTripsByMonthAsync(request.Year, request.Month, ct);
        facilities.Add(BuildTransactionFacility(FacilityCode.TRM, trips
            .OrderBy(t => t.RecordedAt)
            .Select(t => (NameOrUnknown(t.DriverName), new MonthEndTxnRecordDto(
                t.RecordedAt.ToString("MMM d", CultureInfo.InvariantCulture),
                $"Trip #{t.TripNumber}{(string.IsNullOrWhiteSpace(t.Route) ? "" : " · " + t.Route)}",
                t.Fee,
                t.ORNumber), (string?)t.Route))));

        var attendance = await tpmRepository.GetMonthAttendanceAsync(request.Year, request.Month, ct);
        facilities.Add(BuildTransactionFacility(FacilityCode.TPM, attendance
            .Where(a => a.IsPaid)
            .OrderBy(a => a.MarketDate)
            .Select(a => (NameOrUnknown(a.VendorName), new MonthEndTxnRecordDto(
                a.MarketDate.ToString("MMM d", CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(a.Goods) ? "Friday market" : $"Friday market · {a.Goods}",
                a.Fee,
                a.ORNumber), (string?)a.Goods))));

        var ordered = facilities.OrderBy(f => f.Code).ToList();

        var totalCollected = ordered.Sum(f => f.Collected);
        var totalOutstanding = ordered.Sum(f => f.Outstanding);
        var billed = totalCollected + totalOutstanding;
        var overallRate = billed > 0m ? (int)Math.Round(totalCollected / billed * 100m) : 0;

        var label = new DateTime(request.Year, request.Month, 1)
            .ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        return new MonthEndReportDto(
            Year: request.Year,
            Month: request.Month,
            PeriodLabel: label,
            TotalCollected: totalCollected,
            TotalOutstanding: totalOutstanding,
            OverallCollectionRate: overallRate,
            // Paid/unpaid payor counts are a rental-compliance concept; transaction facilities don't contribute.
            TotalPaidCount: ordered.Where(f => f.IsRental).Sum(f => f.PaidCount),
            TotalPartialCount: ordered.Where(f => f.IsRental).Sum(f => f.PartialCount),
            TotalUnpaidCount: ordered.Where(f => f.IsRental).Sum(f => f.UnpaidCount),
            Facilities: ordered);
    }

    /// <summary>
    /// Slaughterhouse owners grouped for the month. Each owner row carries an animal-type summary
    /// (e.g. "2 Carabao · 2 Cow · 1 Hog"), and the expandable detail lists every transaction ordered by
    /// animal type so identical animals sit together while each receipt's OR stays visible for audit.
    /// </summary>
    private static MonthEndFacilityDto BuildSlaughterFacility(IReadOnlyList<SlaughterTransactionDto> transactions)
    {
        var groups = transactions
            .GroupBy(t => NameOrUnknown(t.OwnerName))
            .Select(g =>
            {
                var records = g
                    .OrderBy(t => AnimalLabel(t), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.TransactionDate)
                    .Select(t => new MonthEndTxnRecordDto(
                        t.TransactionDate.ToString("MMM d", CultureInfo.InvariantCulture),
                        $"{t.NumberOfHeads} {AnimalLabel(t)} · ₱{t.RatePerHead:N0}/head",
                        t.TotalAmount,
                        t.ORNumber))
                    .ToList();

                var summary = string.Join(" · ", g
                    .GroupBy(t => AnimalLabel(t))
                    .Select(a => new { Animal = a.Key, Heads = a.Sum(x => x.NumberOfHeads) })
                    .OrderByDescending(a => a.Heads)
                    .ThenBy(a => a.Animal)
                    .Select(a => $"{a.Heads} {a.Animal}"));

                return new MonthEndTxnPayorDto(
                    Payor: g.Key,
                    RecordCount: g.Count(),
                    TotalCollected: g.Sum(x => x.TotalAmount),
                    Records: records,
                    Summary: summary,
                    Quantity: g.Sum(x => x.NumberOfHeads));   // total heads slaughtered
            })
            .OrderByDescending(p => p.TotalCollected)
            .ThenBy(p => p.Payor)
            .ToList();

        var collected = groups.Sum(p => p.TotalCollected);

        return new MonthEndFacilityDto(
            Code: FacilityCode.SLH,
            Name: FacilityName(FacilityCode.SLH),
            IsRental: false,
            Collected: collected,
            Outstanding: 0m,
            CollectionRate: collected > 0m ? 100 : 0,
            PaidCount: 0,
            PartialCount: 0,
            UnpaidCount: 0,
            TotalPayors: groups.Count,
            Payors: Array.Empty<MonthEndPayorDto>(),
            TransactionPayors: groups);
    }

    private static MonthEndFacilityDto BuildTransactionFacility(
        FacilityCode code, IEnumerable<(string Payor, MonthEndTxnRecordDto Record, string? Detail)> rows)
    {
        // GroupBy preserves source order, so the date-sorted input keeps each payor's records chronological.
        var groups = rows
            .GroupBy(r => r.Payor)
            .Select(g => new MonthEndTxnPayorDto(
                Payor: g.Key,
                RecordCount: g.Count(),
                TotalCollected: g.Sum(x => x.Record.Amount),
                Records: g.Select(x => x.Record).ToList(),
                Summary: DistinctDetails(g.Select(x => x.Detail)),   // TRM route(s) / TPM goods
                Quantity: g.Count()))                                // TRM trips / TPM Fridays
            .OrderByDescending(p => p.TotalCollected)
            .ThenBy(p => p.Payor)
            .ToList();

        var collected = groups.Sum(p => p.TotalCollected);

        return new MonthEndFacilityDto(
            Code: code,
            Name: FacilityName(code),
            IsRental: false,
            Collected: collected,
            Outstanding: 0m,
            CollectionRate: collected > 0m ? 100 : 0,
            PaidCount: 0,
            PartialCount: 0,
            UnpaidCount: 0,
            TotalPayors: groups.Count,
            Payors: Array.Empty<MonthEndPayorDto>(),
            TransactionPayors: groups);
    }

    private static string NameOrUnknown(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Unspecified" : name;

    /// <summary>Distinct, non-empty detail labels for a payor (e.g. TPM goods, TRM route(s)).</summary>
    private static string? DistinctDetails(IEnumerable<string?> details)
    {
        var distinct = details
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return distinct.Count == 0 ? null : string.Join(" · ", distinct);
    }

    private static string AnimalLabel(SlaughterTransactionDto t) =>
        !string.IsNullOrWhiteSpace(t.CustomAnimalType) ? t.CustomAnimalType! : t.AnimalType.ToString();

    private static string FacilityName(FacilityCode code) => code switch
    {
        FacilityCode.NPM => "New Public Market",
        FacilityCode.TCC => "Tampak Commercial Center",
        FacilityCode.NCC => "New Commercial Center",
        FacilityCode.BBQ => "Barbecue Stand",
        FacilityCode.ICE => "Iceplant",
        FacilityCode.SLH => "Slaughterhouse",
        FacilityCode.TRM => "Transport Terminal",
        FacilityCode.TPM => "Tabo-an Public Market",
        _ => code.ToString()
    };
}
