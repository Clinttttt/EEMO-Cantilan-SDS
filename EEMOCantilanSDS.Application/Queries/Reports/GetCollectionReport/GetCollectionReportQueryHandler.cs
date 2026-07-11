using System.Globalization;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetCollectionReport;

/// <summary>
/// Assembles the per-facility collection report for the Export Data page. Rental facilities
/// (NPM/TCC/NCC/BBQ/ICE) come from the canonical per-facility stall compliance — its Section field
/// already carries the NPM market section OR the NCC area location, and AbsentDays drives NPM full-month
/// coverage. Service facilities (SLH/TRM/TPM) come from their own month records as structured rows.
/// No new aggregation or financial logic is introduced; figures reconcile to the Month-End report.
/// </summary>
public class GetCollectionReportQueryHandler(
    IFacilityReportsRepository reportsRepository,
    ISlaughterRepository slaughterRepository,
    ITrmRepository trmRepository,
    ITpmRepository tpmRepository,
    IFacilityRepository facilityRepository,
    IFeeRateResolver feeRateResolver
) : IRequestHandler<GetCollectionReportQuery, Result<CollectionReportDto>>
{
    private static readonly FacilityCode[] StallFacilities =
        { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE,
          FacilityCode.Custom1, FacilityCode.Custom2, FacilityCode.Custom3, FacilityCode.Custom4, FacilityCode.Custom5 };

    public async Task<Result<CollectionReportDto>> Handle(GetCollectionReportQuery request, CancellationToken ct)
    {
        var year = request.Year;
        var month = request.Month;
        var periodLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        // Resolve the municipality's NPM rates as of the report month (falls back to the ordinance
        // constants, so Cantilan figures are unchanged). The 30-day full-month reference = daily × 30.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var npmDaily = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, asOf);
        var fishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, asOf);
        var npmMonthly = npmDaily * 30m;

        // Only the facilities the current tenant operates are reported (no phantom zero facilities).
        // Cantilan has all eight, so its report is unchanged.
        var facilityNames = await facilityRepository.GetFacilityNamesAsync(ct);
        var tenantCodes = facilityNames.Keys.ToHashSet();

        var facilities = new List<CollectionFacilityDto>();

        // ── Rental facilities — per-stall compliance (Section = NPM market section / NCC area location) ──
        // Per-stall NPM fish kilos (₱1/kg) for the month — surfaced as a separate extra charge on the report.
        var npmFishByStall = await reportsRepository.GetNpmFishKilosByStallAsync(year, month, ct);

        foreach (var code in StallFacilities.Where(tenantCodes.Contains))
        {
            var report = await reportsRepository.GetFacilityReportsAsync(code, ReportPeriod.Monthly, year, month, null, ct);
            var isNpm = code == FacilityCode.NPM;

            var rentals = report.StallCompliance.Select(s =>
            {
                // NPM full-month coverage (₱900 reference less excused/absent days), same as Month-End.
                var coverage = isNpm ? Math.Max(0m, npmMonthly - s.AbsentDays * npmDaily) : 0m;
                var coverageBalance = isNpm ? Math.Max(0m, coverage - s.AmountPaid) : 0m;
                var rate = isNpm ? (s.DailyRate > 0 ? s.DailyRate : npmDaily) : s.MonthlyRate;
                var fishKilos = isNpm ? npmFishByStall.GetValueOrDefault(s.StallId) : 0m;
                var fishFee = fishKilos * fishRate;
                return new CollectionRentalRowDto(
                    s.StallNo, s.Section ?? string.Empty, s.Occupant, rate, s.Status,
                    s.AmountPaid, s.Balance, s.ORNumber, coverage, coverageBalance, fishKilos, fishFee);
            }).ToList();

            facilities.Add(new CollectionFacilityDto(
                code, ReportName(code, facilityNames), Model(code), IsRental: true,
                report.TotalRevenue, report.PendingPaymentAmount,
                rentals, Array.Empty<CollectionTxnRowDto>()));
        }

        // ── Slaughterhouse (per-head) ──
        var slaughter = await slaughterRepository.GetTransactionsByMonthAsync(year, month, ct);
        var slhRows = slaughter
            .OrderBy(t => t.OwnerName).ThenBy(t => t.TransactionDate)
            .Select(t => new CollectionTxnRowDto(
                NameOrUnknown(t.OwnerName), t.TransactionDate.ToString("MMM d", CultureInfo.InvariantCulture),
                string.Empty, AnimalLabel(t), t.NumberOfHeads, t.RatePerHead, t.ORNumber, t.TotalAmount))
            .ToList();
        facilities.Add(new CollectionFacilityDto(
            FacilityCode.SLH, FacilityName(FacilityCode.SLH), Model(FacilityCode.SLH), IsRental: false,
            slhRows.Sum(r => r.Amount), 0m, Array.Empty<CollectionRentalRowDto>(), slhRows));

        // ── Transport Terminal (per-trip) ──
        var trips = await trmRepository.GetTripsByMonthAsync(year, month, ct);
        var trmRows = trips
            .OrderBy(t => t.DriverName).ThenBy(t => t.RecordedAt)
            .Select(t => new CollectionTxnRowDto(
                NameOrUnknown(t.DriverName), t.RecordedAt.ToString("MMM d", CultureInfo.InvariantCulture),
                $"Trip #{t.TripNumber}", t.Route ?? string.Empty, 0, 0m, t.ORNumber, t.Fee))
            .ToList();
        facilities.Add(new CollectionFacilityDto(
            FacilityCode.TRM, FacilityName(FacilityCode.TRM), Model(FacilityCode.TRM), IsRental: false,
            trmRows.Sum(r => r.Amount), 0m, Array.Empty<CollectionRentalRowDto>(), trmRows));

        // ── Tabo-an Public Market (weekly, paid attendance only) ──
        var attendance = await tpmRepository.GetMonthAttendanceAsync(year, month, ct);
        var tpmRows = attendance
            .Where(a => a.IsPaid)
            .OrderBy(a => a.VendorName).ThenBy(a => a.MarketDate)
            .Select(a => new CollectionTxnRowDto(
                NameOrUnknown(a.VendorName), a.MarketDate.ToString("MMM d", CultureInfo.InvariantCulture),
                string.Empty, a.Goods ?? string.Empty, 0, 0m, a.ORNumber, a.Fee))
            .ToList();
        facilities.Add(new CollectionFacilityDto(
            FacilityCode.TPM, FacilityName(FacilityCode.TPM), Model(FacilityCode.TPM), IsRental: false,
            tpmRows.Sum(r => r.Amount), 0m, Array.Empty<CollectionRentalRowDto>(), tpmRows));

        var ordered = facilities.Where(f => tenantCodes.Contains(f.Code)).OrderBy(f => f.Code).ToList();
        return Result<CollectionReportDto>.Success(new CollectionReportDto(periodLabel, PhilippineTime.Today, ordered));
    }

    private static string NameOrUnknown(string? name) => string.IsNullOrWhiteSpace(name) ? "Unspecified" : name;

    private static string AnimalLabel(SlaughterTransactionDto t) =>
        !string.IsNullOrWhiteSpace(t.CustomAnimalType) ? t.CustomAnimalType! : t.AnimalType.ToString();

    private static string Model(FacilityCode code) => code switch
    {
        FacilityCode.NPM => "Daily stall",
        FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE => "Monthly rental",
        FacilityCode.SLH => "Per-head",
        FacilityCode.TRM => "Per-trip",
        FacilityCode.TPM => "Weekly market",
        _ when FacilityCatalog.IsCustom(code) => "Monthly rental",
        _ => "—"
    };

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

    // Head-named custom facilities use their stored name; canonical facilities keep the fixed label.
    private static string ReportName(FacilityCode code, IReadOnlyDictionary<FacilityCode, string> names) =>
        names.TryGetValue(code, out var n) && !string.IsNullOrWhiteSpace(n) ? n : FacilityName(code);
}
