using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class CollectorRepository(AppDbContext context) : ICollectorRepository
{
    public async Task<CollectorUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<MobileCollectorRecordDto>> GetCollectorRecordsAsync(
        Guid collectorId, FacilityCode? facility, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        // UTC window for timestamp-based sources (PaymentRecords/TrmTrips); DateOnly sources compare
        // against the business date directly. FacilityName is filled by the handler from the canonical
        // facility names, so a blank placeholder is fine here.
        var (startUtc, _) = PhilippineTime.DayUtcRange(fromDate);
        var (_, endUtc) = PhilippineTime.DayUtcRange(toDate);

        // The Records feed shows the collector's OWN collections plus admin/office-recorded entries
        // (CollectorId == null) at the facilities they're assigned to — the latter are tagged so
        // attribution stays clear. Admin entries outside their assignments are never shown.
        var assignedCodes = await context.CollectorFacilityAssignments
            .Where(a => a.CollectorId == collectorId)
            .Select(a => a.FacilityCode)
            .ToListAsync(cancellationToken);
        var assignedSet = assignedCodes.ToHashSet();

        var all = facility is null;
        var results = new List<MobileCollectorRecordDto>();

        // ── Monthly stall rentals (TCC/NCC/BBQ/ICE) — collection event = PaidAt ──
        if (all || facility is FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE or FacilityCode.NPM)
        {
            var q = context.PaymentRecords.AsNoTracking()
                .Where(p => p.Status != PaymentStatus.Unpaid
                    && (p.CollectorId == collectorId
                        || (p.CollectorId == null && assignedCodes.Contains(p.Stall!.Facility!.Code))));
            if (!all) q = q.Where(p => p.Stall!.Facility!.Code == facility);

            var rows = await q
                .Where(p => (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= startUtc
                         && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < endUtc)
                .Select(p => new
                {
                    p.ORNumber,
                    Payor = p.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                    Code = p.Stall.Facility!.Code,
                    p.Stall.StallNo,
                    p.Stall.Section,
                    p.Status,
                    p.BaseRentalAmount,
                    p.ElecAmount,
                    p.WaterAmount,
                    p.FishKilos,
                    p.PartialAmount,
                    IsAdmin = p.CollectorId == null,
                    When = p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt
                })
                .ToListAsync(cancellationToken);

            results.AddRange(rows.Select(r =>
            {
                var full = r.BaseRentalAmount + (r.ElecAmount ?? 0) + (r.WaterAmount ?? 0)
                           + ((r.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo);
                var partial = r.Status == PaymentStatus.Partial;
                return new MobileCollectorRecordDto(
                    r.ORNumber ?? "—", r.Payor ?? "—", r.Code, string.Empty, r.StallNo,
                    "Stall Rental", full, partial ? r.PartialAmount : full, partial, PhilippineTime.ToPhilippineTime(r.When), r.Section, r.FishKilos,
                    r.IsAdmin);
            }));
        }

        // ── NPM daily collections — business date = CollectionDate ──
        if (all || facility is FacilityCode.NPM)
        {
            var npmAssigned = assignedSet.Contains(FacilityCode.NPM);
            // Include PAID days and ABSENT/excused days (₱0, no OR) so the collector sees their full
            // daily activity — an absence they marked should still appear on the feed.
            var rows = await context.DailyCollections.AsNoTracking()
                .Where(d => (d.IsPaid || d.IsAbsent)
                         && d.CollectionDate >= fromDate && d.CollectionDate <= toDate
                         && (d.CollectorId == collectorId || (npmAssigned && d.CollectorId == null)))
                .Select(d => new
                {
                    d.ORNumber,
                    Payor = d.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                    Code = d.Stall.Facility!.Code,
                    d.StallId,
                    d.CollectionDate,
                    d.Stall.StallNo,
                    d.Stall.Section,
                    d.DailyFee,
                    d.FishKilos,
                    d.IsAbsent,
                    IsAdmin = d.CollectorId == null,
                    When = d.UpdatedAt ?? d.CreatedAt
                })
                .ToListAsync(cancellationToken);

            // Attach each stall's electricity & water bill for the record's month — shown in the DETAIL
            // only (kept off the card so a payor stays as one entry). Fetched once for the whole range.
            var billMap = await BuildUtilityDetailMapAsync(
                rows.Select(r => r.StallId).Distinct().ToList(), fromDate, toDate, cancellationToken);

            results.AddRange(rows.Select(d =>
            {
                var amount = d.DailyFee + ((d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo);
                var util = billMap.GetValueOrDefault((d.StallId, d.CollectionDate.Year, d.CollectionDate.Month));
                return d.IsAbsent
                    ? new MobileCollectorRecordDto(
                        "—", d.Payor ?? "—", d.Code, string.Empty, d.StallNo,
                        "Absent / Excused", 0m, 0m, false, PhilippineTime.ToPhilippineTime(d.When), d.Section, null,
                        IsAdminRecorded: false, IsAbsent: true, Utility: util)
                    : new MobileCollectorRecordDto(
                        d.ORNumber ?? "—", d.Payor ?? "—", d.Code, string.Empty, d.StallNo,
                        "Daily Fee", amount, amount, false, PhilippineTime.ToPhilippineTime(d.When), d.Section, d.FishKilos,
                        d.IsAdmin, IsAbsent: false, Utility: util);
            }));
        }

        // ── Slaughterhouse — business date = TransactionDate ──
        if (all || facility is FacilityCode.SLH)
        {
            var slhAssigned = assignedSet.Contains(FacilityCode.SLH);
            var rows = await context.SlaughterTransactions.AsNoTracking()
                .Where(s => (s.CollectorId == collectorId || (slhAssigned && s.CollectorId == null))
                         && s.TransactionDate >= fromDate && s.TransactionDate <= toDate)
                .Select(s => new
                {
                    s.ORNumber,
                    s.OwnerName,
                    s.AnimalType,
                    s.CustomAnimalType,
                    s.RatePerHead,
                    s.NumberOfHeads,
                    s.TransactionDate,
                    IsAdmin = s.CollectorId == null,
                    When = s.UpdatedAt ?? s.CreatedAt
                })
                .ToListAsync(cancellationToken);

            // One slaughter OR covers a customer's whole visit, so several animal rows can share a
            // receipt. Group per receipt → ONE feed card per receipt (total amount), with the animal
            // breakdown carried for the detail popup. Key by OR when present, else by owner + date
            // (an unreceipted batch captured in one visit).
            var slhGroups = rows.GroupBy(r => !string.IsNullOrWhiteSpace(r.ORNumber)
                ? $"OR::{r.ORNumber}"
                : $"OD::{r.OwnerName}|{r.TransactionDate:yyyy-MM-dd}");

            results.AddRange(slhGroups.Select(g =>
            {
                var total = g.Sum(x => x.RatePerHead * x.NumberOfHeads);
                var lines = g
                    .OrderBy(x => string.IsNullOrWhiteSpace(x.CustomAnimalType) ? x.AnimalType.ToString() : x.CustomAnimalType)
                    .Select(x => new MobileSlaughterLineDto(
                        string.IsNullOrWhiteSpace(x.CustomAnimalType) ? x.AnimalType.ToString() : x.CustomAnimalType!,
                        x.NumberOfHeads,
                        x.RatePerHead,
                        x.RatePerHead * x.NumberOfHeads))
                    .ToList();
                var first = g.First();
                return new MobileCollectorRecordDto(
                    first.ORNumber ?? "—", first.OwnerName, FacilityCode.SLH, string.Empty, null,
                    "Slaughter", total, total, false, PhilippineTime.ToPhilippineTime(g.Max(x => x.When)),
                    IsAdminRecorded: g.All(x => x.IsAdmin),
                    SlaughterLines: lines);
            }));
        }

        // ── Transport terminal — collection event = RecordedAt ──
        if (all || facility is FacilityCode.TRM)
        {
            var trmAssigned = assignedSet.Contains(FacilityCode.TRM);
            var rows = await context.TrmTrips.AsNoTracking()
                .Where(t => (t.CollectorId == collectorId || (trmAssigned && t.CollectorId == null))
                         && t.RecordedAt >= startUtc && t.RecordedAt < endUtc)
                .Select(t => new { t.ORNumber, t.DriverName, t.Fee, t.RecordedAt, IsAdmin = t.CollectorId == null })
                .ToListAsync(cancellationToken);

            results.AddRange(rows.Select(t => new MobileCollectorRecordDto(
                t.ORNumber ?? "—", t.DriverName, FacilityCode.TRM, string.Empty, null,
                "Terminal Trip", t.Fee, t.Fee, false, PhilippineTime.ToPhilippineTime(t.RecordedAt),
                IsAdminRecorded: t.IsAdmin)));
        }

        // ── Tabo-an market — business date = MarketDate ──
        if (all || facility is FacilityCode.TPM)
        {
            var tpmAssigned = assignedSet.Contains(FacilityCode.TPM);
            var rows = await context.TpmAttendances.AsNoTracking()
                .Where(a => (a.CollectorId == collectorId || (tpmAssigned && a.CollectorId == null))
                         && a.IsPaid
                         && a.MarketDate >= fromDate && a.MarketDate <= toDate)
                .Select(a => new
                {
                    a.ORNumber,
                    Vendor = a.Vendor!.VendorName,
                    a.Fee,
                    IsAdmin = a.CollectorId == null,
                    When = a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt
                })
                .ToListAsync(cancellationToken);

            results.AddRange(rows.Select(a => new MobileCollectorRecordDto(
                a.ORNumber ?? "—", a.Vendor, FacilityCode.TPM, string.Empty, null,
                "Market Day", a.Fee, a.Fee, false, PhilippineTime.ToPhilippineTime(a.When),
                IsAdminRecorded: a.IsAdmin)));
        }

        return results.OrderByDescending(r => r.CollectedAt).ToList();
    }

    // Builds a (stall, year, month) → utility bill map for the NPM record detail. Only bills that carry
    // a charge are included; electricity/water amounts are computed in memory (they are not stored).
    private async Task<Dictionary<(Guid StallId, int Year, int Month), MobileRecordUtilityDto>> BuildUtilityDetailMapAsync(
        IReadOnlyList<Guid> stallIds, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var map = new Dictionary<(Guid, int, int), MobileRecordUtilityDto>();
        if (stallIds.Count == 0)
            return map;

        var bills = await context.UtilityBills.AsNoTracking()
            .Where(b => stallIds.Contains(b.StallId)
                && (b.BillingYear > fromDate.Year || (b.BillingYear == fromDate.Year && b.BillingMonth >= fromDate.Month))
                && (b.BillingYear < toDate.Year || (b.BillingYear == toDate.Year && b.BillingMonth <= toDate.Month)))
            .Select(b => new
            {
                b.StallId, b.BillingYear, b.BillingMonth,
                b.ElecPreviousReading, b.ElecCurrentReading, b.ElecRatePerKwh, b.ElecStatus, b.ElecPartialAmount, b.ElecORNumber,
                b.WaterPreviousReading, b.WaterCurrentReading, b.WaterRatePerCubicMeter, b.WaterStatus, b.WaterPartialAmount, b.WaterORNumber
            })
            .ToListAsync(ct);

        foreach (var b in bills)
        {
            var ec = Math.Max(0m, b.ElecCurrentReading - b.ElecPreviousReading) * b.ElecRatePerKwh;
            var wc = Math.Max(0m, b.WaterCurrentReading - b.WaterPreviousReading) * b.WaterRatePerCubicMeter;
            if (ec <= 0m && wc <= 0m)
                continue; // no charge → nothing to show in the detail

            var ep = b.ElecStatus == PaymentStatus.Paid ? ec : b.ElecStatus == PaymentStatus.Partial ? b.ElecPartialAmount : 0m;
            var wp = b.WaterStatus == PaymentStatus.Paid ? wc : b.WaterStatus == PaymentStatus.Partial ? b.WaterPartialAmount : 0m;

            map[(b.StallId, b.BillingYear, b.BillingMonth)] = new MobileRecordUtilityDto(
                ec, b.ElecStatus.ToString(), ep, ec - ep, b.ElecORNumber,
                wc, b.WaterStatus.ToString(), wp, wc - wp, b.WaterORNumber,
                ec + wc, ep + wp, (ec + wc) - (ep + wp));
        }

        return map;
    }

    public async Task<MobileCollectorReportDto> GetCollectorReportAsync(
        Guid collectorId, IReadOnlyCollection<FacilityCode> facilities, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        var selectedFacilities = facilities.Distinct().ToList();
        var selectedSet = selectedFacilities.ToHashSet();
        var year = fromDate.Year;
        var month = fromDate.Month;
        var dailyReportMode = selectedFacilities.Count > 0 && selectedFacilities.All(IsDailyReportFacility);

        var facilityNames = await context.Facilities
            .AsNoTracking()
            .Where(f => selectedSet.Contains(f.Code))
            .ToDictionaryAsync(f => f.Code, f => f.Name, cancellationToken);

        var transactions = new List<CollectorReportTransaction>();
        var payees = new List<MobileReportPayeeSummaryDto>();
        var openItemsByDate = new Dictionary<DateOnly, int>();
        // Absent (NPM daily) + excused (monthly-facility exception) counts — kept SEPARATE from
        // paid/partial/unpaid so an excused payor is never presented as a debt.
        var absentExcusedByFacility = new Dictionary<FacilityCode, int>();
        var absentExcusedByDate = new Dictionary<DateOnly, int>();
        var absentExcusedTotal = 0;
        var absentExcusedRows = new List<MobileReportAbsentExcusedDto>();
        var (startUtc, _) = PhilippineTime.DayUtcRange(fromDate);
        var (_, endUtc) = PhilippineTime.DayUtcRange(toDate);

        // Obligation/open-items are assessed only up to the caller's end (today for the current month
        // = toDate). COLLECTED money, however, must include every paid collection in the month — even
        // days paid in advance (CollectionDate after today). So daily collections range to month-end.
        var collectionEnd = new DateOnly(fromDate.Year, fromDate.Month, DateTime.DaysInMonth(fromDate.Year, fromDate.Month));
        var monthStart = new DateOnly(year, month, 1);

        if (selectedSet.Contains(FacilityCode.NPM))
        {
            var npmStalls = await context.Stalls
                .AsNoTracking()
                .Include(s => s.Contracts)
                .Include(s => s.DailyCollections.Where(d => d.CollectionDate >= fromDate && d.CollectionDate <= collectionEnd
                    && (d.CollectorId == collectorId || d.CollectorId == null)))
                .Where(s => s.Facility!.Code == FacilityCode.NPM
                    && s.Status == StallStatus.Active
                    && s.Section.HasValue
                    && s.Contracts.Any(c => c.IsActive))
                .OrderBy(s => s.Section)
                .ThenBy(s => s.StallNo)
                .ToListAsync(cancellationToken);

            var npmStallsById = npmStalls.ToDictionary(s => s.Id);

            // Absent (excused) NPM days in the reporting window — counted separately, never as unpaid,
            // and captured as rows for the Absent/Excused detail view.
            foreach (var s in npmStalls)
            {
                foreach (var d in s.DailyCollections.Where(d => d.IsAbsent && d.CollectionDate >= fromDate && d.CollectionDate <= toDate))
                {
                    // Only count an absence on a date the stall's contract actually covers (skip stray
                    // absences on days outside any contract term), and attribute it to that contract.
                    var cov = s.Contracts.Where(c => c.IsCollectableOn(d.CollectionDate)).OrderByDescending(c => c.EffectivityDate).FirstOrDefault();
                    if (cov is null) continue;
                    var absentPayor = string.IsNullOrWhiteSpace(cov.ActualOccupant) ? "No active occupant" : cov.ActualOccupant;
                    absentExcusedRows.Add(new MobileReportAbsentExcusedDto(
                        FacilityCode.NPM, FacilityName(FacilityCode.NPM, facilityNames), s.StallNo, absentPayor,
                        d.CollectionDate, "NPM daily absence", null));
                    absentExcusedByFacility[FacilityCode.NPM] = absentExcusedByFacility.GetValueOrDefault(FacilityCode.NPM) + 1;
                    absentExcusedByDate[d.CollectionDate] = absentExcusedByDate.GetValueOrDefault(d.CollectionDate) + 1;
                    absentExcusedTotal++;
                }
            }
            var npmStallIds = npmStallsById.Keys.ToList();
            var npmPaymentRecords = await context.PaymentRecords
                .AsNoTracking()
                .Where(p => npmStallIds.Contains(p.StallId)
                    && (p.CollectorId == collectorId || p.CollectorId == null))
                .ToListAsync(cancellationToken);

            var periodNpmPaymentRecords = npmPaymentRecords
                .Where(p => p.Status != PaymentStatus.Unpaid
                    && IsPaymentInDateRange(p.BillingYear, p.BillingMonth, fromDate, toDate))
                .ToList();

            var stallsWithMonthlyPayments = periodNpmPaymentRecords
                .Select(p => p.StallId)
                .ToHashSet();
            var monthlyPaymentStallIds = stallsWithMonthlyPayments.ToList();

            var npmCollections = await context.DailyCollections
                .AsNoTracking()
                .Where(d => d.IsPaid
                    && d.Stall!.Facility!.Code == FacilityCode.NPM
                    && d.CollectionDate >= fromDate
                    && d.CollectionDate <= collectionEnd
                    && !monthlyPaymentStallIds.Contains(d.StallId)
                    && (d.CollectorId == collectorId || d.CollectorId == null))
                .Select(d => new
                {
                    d.ORNumber,
                    Payor = d.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                    d.StallId,
                    d.Stall.StallNo,
                    d.CollectionDate,
                    d.DailyFee,
                    d.FishKilos,
                    When = d.UpdatedAt ?? d.CreatedAt
                })
                .ToListAsync(cancellationToken);

            transactions.AddRange(npmCollections.Select(d => new CollectorReportTransaction(
                FacilityCode.NPM,
                FacilityName(FacilityCode.NPM, facilityNames),
                d.StallNo,
                d.Payor ?? "No active occupant",
                d.CollectionDate,
                d.DailyFee + d.FishKilos.GetValueOrDefault() * FeeRates.NpmFishFeePerKilo,
                false,
                d.When,
                d.ORNumber)));

            transactions.AddRange(periodNpmPaymentRecords.Select(p =>
            {
                var stall = npmStallsById[p.StallId];
                var contract = stall.Contracts
                    .Where(c => c.OverlapsPeriod(fromDate, collectionEnd))
                    .OrderByDescending(c => c.EffectivityDate)
                    .FirstOrDefault();
                var collectedAt = p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt;
                var collectedDate = DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(collectedAt).Date);

                return new CollectorReportTransaction(
                    FacilityCode.NPM,
                    FacilityName(FacilityCode.NPM, facilityNames),
                    stall.StallNo,
                    string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? "No active occupant" : contract.ActualOccupant,
                    ClampDate(collectedDate, fromDate, toDate),
                    RecognizedNpmPaymentRevenue(p, fromDate, toDate, stall),
                    p.Status == PaymentStatus.Partial,
                    collectedAt,
                    p.ORNumber);
            }).Where(t => t.Amount > 0m));

            payees.AddRange(npmStalls
                .Where(s => s.Contracts.Any(c => c.OverlapsPeriod(fromDate, collectionEnd)))
                .Select(s =>
            {
                var contract = s.Contracts
                    .Where(c => c.OverlapsPeriod(fromDate, collectionEnd))
                    .OrderByDescending(c => c.EffectivityDate)
                    .FirstOrDefault();

                var dailyRate = s.DailyRate ?? FeeRates.NpmDailyFee;
                var stallPayments = periodNpmPaymentRecords
                    .Where(p => p.StallId == s.Id)
                    .ToList();
                var paidCollections = stallsWithMonthlyPayments.Contains(s.Id)
                    ? new List<DailyCollection>()
                    : s.DailyCollections
                        .Where(d => d.IsPaid && d.CollectionDate >= fromDate && d.CollectionDate <= collectionEnd)
                        .ToList();

                // Obligation + collected are assessed over the FULL month — matching the web reports
                // (FacilityReportsRepository) and dashboard — so balances and Partial status reconcile
                // across web and mobile. Fish fees (₱/kg) are NOT part of the rental obligation, so the
                // per-payee Amount Paid is rental-only (like the web's per-stall column); fish revenue
                // still appears in the Total Collection.
                var collectableDays = CountNpmCollectableDays(s, fromDate, collectionEnd);
                var monthlyRentalPaid = stallPayments.Sum(p => RecognizedNpmDailyFeeRevenue(p, fromDate, collectionEnd, s));
                var rentalPaid = monthlyRentalPaid + paidCollections.Count * dailyRate;
                var assessed = collectableDays * dailyRate;
                var balance = Math.Max(0m, assessed - rentalPaid);
                var status = balance <= 0m && collectableDays > 0
                    ? PaymentStatus.Paid
                    : rentalPaid > 0m ? PaymentStatus.Partial : PaymentStatus.Unpaid;

                return new MobileReportPayeeSummaryDto(
                    string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? "No active occupant" : contract.ActualOccupant,
                    contract?.NameOnContract,
                    FacilityCode.NPM,
                    FacilityName(FacilityCode.NPM, facilityNames),
                    s.StallNo,
                    GetSectionName(s.Section),
                    status,
                    assessed,
                    rentalPaid,
                    balance,
                    0,
                    0,
                    null,
                    stallPayments.OrderByDescending(p => p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt).Select(p => p.ORNumber).FirstOrDefault()
                        ?? paidCollections.OrderByDescending(d => d.CollectionDate).Select(d => d.ORNumber).FirstOrDefault());
            }));

            for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            {
                var openItems = npmStalls.Count(s => IsStallCollectableOn(s, date)
                    && !s.DailyCollections.Any(d => d.IsPaid && d.CollectionDate == date)
                    && !periodNpmPaymentRecords.Any(p => p.StallId == s.Id && NpmPaymentCoversDate(p, date, s)));

                if (openItems > 0)
                    openItemsByDate[date] = openItems;
            }
        }

        var monthlyFacilities = selectedFacilities.Where(IsMonthlyRentalFacility).ToList();
        if (monthlyFacilities.Count > 0)
        {
            var monthlyRows = await context.PaymentRecords
                .AsNoTracking()
                .Where(p => p.Status != PaymentStatus.Unpaid
                    && p.Stall!.Facility != null
                    && monthlyFacilities.Contains(p.Stall.Facility.Code)
                    && (p.CollectorId == collectorId || p.CollectorId == null)
                    && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) >= startUtc
                    && (p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt) < endUtc)
                .Select(p => new
                {
                    p.ORNumber,
                    Facility = p.Stall!.Facility!.Code,
                    Payor = p.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                    p.Stall.StallNo,
                    p.Status,
                    p.BaseRentalAmount,
                    p.ElecAmount,
                    p.WaterAmount,
                    p.FishKilos,
                    p.PartialAmount,
                    When = p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt
                })
                .ToListAsync(cancellationToken);

            transactions.AddRange(monthlyRows.Select(p =>
            {
                var fullAmount = p.BaseRentalAmount
                    + p.ElecAmount.GetValueOrDefault()
                    + p.WaterAmount.GetValueOrDefault()
                    + p.FishKilos.GetValueOrDefault() * FeeRates.NpmFishFeePerKilo;
                var paidAmount = p.Status == PaymentStatus.Partial ? p.PartialAmount : fullAmount;

                return new CollectorReportTransaction(
                    p.Facility,
                    FacilityName(p.Facility, facilityNames),
                    p.StallNo,
                    p.Payor ?? "No active occupant",
                    DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(p.When).Date),
                    paidAmount,
                    p.Status == PaymentStatus.Partial,
                    p.When,
                    p.ORNumber);
            }));

            var monthlyStalls = await context.Stalls
                .AsNoTracking()
                .Include(s => s.Facility)
                .Include(s => s.Contracts)
                .Include(s => s.PaymentRecords.Where(p => p.BillingYear == year && p.BillingMonth == month
                    && (p.CollectorId == collectorId || p.CollectorId == null)))
                .Where(s => s.Facility != null
                    && monthlyFacilities.Contains(s.Facility.Code)
                    && s.Status == StallStatus.Active
                    && s.Contracts.Any(c => c.IsActive))
                .OrderBy(s => s.Facility!.Code)
                .ThenBy(s => s.StallNo)
                .ToListAsync(cancellationToken);

            // Excused monthly-rental stalls for this billing month (₱0 owed) — excluded from the payee
            // list so they are never counted as unpaid, and surfaced separately as absent/excused.
            var monthlyStallIds = monthlyStalls.Select(s => s.Id).ToList();
            var monthlyStallById = monthlyStalls.ToDictionary(s => s.Id);
            var exceptions = await context.StallMonthlyExceptions
                .AsNoTracking()
                .Where(e => monthlyStallIds.Contains(e.StallId) && e.BillingYear == year && e.BillingMonth == month)
                .Select(e => new { e.StallId, e.Reason, e.Remarks })
                .ToListAsync(cancellationToken);
            var excusedStallIds = exceptions.Select(e => e.StallId).ToHashSet();
            if (exceptions.Count > 0)
            {
                var monthPeriod = new DateOnly(year, month, 1);
                foreach (var e in exceptions)
                {
                    if (!monthlyStallById.TryGetValue(e.StallId, out var s)) continue;
                    var contract = s.Contracts.Where(c => c.OverlapsPeriod(monthStart, collectionEnd)).OrderByDescending(c => c.EffectivityDate).FirstOrDefault();
                    if (contract is null) continue; // exception for a month no contract covers → not a valid excused row
                    var code = s.Facility!.Code;
                    var payor = string.IsNullOrWhiteSpace(contract.ActualOccupant) ? "No active occupant" : contract.ActualOccupant;
                    var reason = string.IsNullOrWhiteSpace(e.Remarks) ? e.Reason.ToString() : $"{e.Reason} — {e.Remarks}";
                    absentExcusedRows.Add(new MobileReportAbsentExcusedDto(
                        code, FacilityName(code, facilityNames), s.StallNo, payor, monthPeriod, "Monthly exception", reason));
                    absentExcusedByFacility[code] = absentExcusedByFacility.GetValueOrDefault(code) + 1;
                    absentExcusedByDate[monthPeriod] = absentExcusedByDate.GetValueOrDefault(monthPeriod) + 1;
                    absentExcusedTotal++;
                }
            }

            payees.AddRange(monthlyStalls
                .Where(s => s.Contracts.Any(c => c.OverlapsPeriod(monthStart, collectionEnd)) && !excusedStallIds.Contains(s.Id))
                .Select(s =>
            {
                var facility = s.Facility!.Code;
                var contract = s.Contracts
                    .Where(c => c.OverlapsPeriod(monthStart, collectionEnd))
                    .OrderByDescending(c => c.EffectivityDate)
                    .FirstOrDefault();
                var record = s.PaymentRecords.FirstOrDefault();
                var status = record?.Status ?? PaymentStatus.Unpaid;
                var amountPaid = record?.AmountPaid ?? 0m;
                var balance = record is null ? s.MonthlyRate : record.BalanceDue;

                return new MobileReportPayeeSummaryDto(
                    string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? "No active occupant" : contract.ActualOccupant,
                    contract?.NameOnContract,
                    facility,
                    FacilityName(facility, facilityNames),
                    s.StallNo,
                    GetAreaLabel(s),
                    status,
                    s.MonthlyRate,
                    amountPaid,
                    balance,
                    0,
                    0,
                    null,
                    record?.ORNumber);
            }));
        }

        if (selectedSet.Contains(FacilityCode.SLH))
        {
            var slaughterRows = await context.SlaughterTransactions
                .AsNoTracking()
                .Where(s => (s.CollectorId == collectorId || s.CollectorId == null)
                    && s.TransactionDate >= fromDate && s.TransactionDate <= toDate)
                .Select(s => new { s.OwnerName, s.RatePerHead, s.NumberOfHeads, s.TransactionDate, When = s.UpdatedAt ?? s.CreatedAt, s.ORNumber })
                .ToListAsync(cancellationToken);

            transactions.AddRange(slaughterRows.Select(s => new CollectorReportTransaction(
                FacilityCode.SLH,
                FacilityName(FacilityCode.SLH, facilityNames),
                null,
                s.OwnerName,
                s.TransactionDate,
                s.RatePerHead * s.NumberOfHeads,
                false,
                s.When,
                s.ORNumber)));
        }

        if (selectedSet.Contains(FacilityCode.TRM))
        {
            var tripRows = await context.TrmTrips
                .AsNoTracking()
                .Where(t => (t.CollectorId == collectorId || t.CollectorId == null)
                    && t.RecordedAt >= startUtc && t.RecordedAt < endUtc)
                .Select(t => new { t.DriverName, t.Fee, t.RecordedAt, t.ORNumber })
                .ToListAsync(cancellationToken);

            transactions.AddRange(tripRows.Select(t => new CollectorReportTransaction(
                FacilityCode.TRM,
                FacilityName(FacilityCode.TRM, facilityNames),
                null,
                t.DriverName,
                DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(t.RecordedAt).Date),
                t.Fee,
                false,
                t.RecordedAt,
                t.ORNumber)));
        }

        if (selectedSet.Contains(FacilityCode.TPM))
        {
            var tpmRows = await context.TpmAttendances
                .AsNoTracking()
                .Where(a => a.IsPaid
                    && (a.CollectorId == collectorId || a.CollectorId == null)
                    && a.MarketDate >= fromDate
                    && a.MarketDate <= toDate)
                .Select(a => new { Payor = a.Vendor!.VendorName, a.Fee, a.MarketDate, When = a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt, a.ORNumber })
                .ToListAsync(cancellationToken);

            transactions.AddRange(tpmRows.Select(a => new CollectorReportTransaction(
                FacilityCode.TPM,
                FacilityName(FacilityCode.TPM, facilityNames),
                null,
                a.Payor,
                a.MarketDate,
                a.Fee,
                false,
                a.When,
                a.ORNumber)));
        }

        var transactionStats = transactions
            .GroupBy(t => ReportPayeeKey(t.FacilityCode, t.StallNo, t.PayorName))
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Count = g.Count(),
                    PartialCount = g.Count(t => t.IsPartial),
                    // "Last collection" is the latest business date (collection/payment date), NOT the
                    // record timestamp — bulk-marked rows share a CreatedAt, so the timestamp would
                    // wrongly pin the date to when the batch was entered.
                    LastCollectedAt = g.Max(t => t.PeriodDate).ToDateTime(TimeOnly.MinValue),
                    ORNumber = g.OrderByDescending(t => t.PeriodDate).Select(t => t.ORNumber).FirstOrDefault()
                });

        payees = payees
            .Select(p =>
            {
                var key = ReportPayeeKey(p.FacilityCode, p.StallNo, p.PayorName);
                return transactionStats.TryGetValue(key, out var stats)
                    ? p with
                    {
                        TransactionCount = stats.Count,
                        PartialCount = stats.PartialCount,
                        LastCollectedAt = stats.LastCollectedAt,
                        ORNumber = string.IsNullOrWhiteSpace(p.ORNumber) ? stats.ORNumber : p.ORNumber
                    }
                    : p;
            })
            .ToList();

        var existingPayeeKeys = payees.Select(p => ReportPayeeKey(p.FacilityCode, p.StallNo, p.PayorName)).ToHashSet();
        payees.AddRange(transactions
            .Where(t => !existingPayeeKeys.Contains(ReportPayeeKey(t.FacilityCode, t.StallNo, t.PayorName)))
            .GroupBy(t => new { t.FacilityCode, t.FacilityName, t.StallNo, t.PayorName })
            .Select(g => new MobileReportPayeeSummaryDto(
                g.Key.PayorName,
                null,
                g.Key.FacilityCode,
                g.Key.FacilityName,
                g.Key.StallNo,
                string.Empty,
                PaymentStatus.Paid,
                g.Sum(t => t.Amount),
                g.Sum(t => t.Amount),
                0m,
                g.Count(),
                g.Count(t => t.IsPartial),
                g.Max(t => t.PeriodDate).ToDateTime(TimeOnly.MinValue),
                g.OrderByDescending(t => t.PeriodDate).Select(t => t.ORNumber).FirstOrDefault())));

        var facilitySummaries = selectedFacilities
            .Select(f =>
            {
                var facilityPayees = payees.Where(p => p.FacilityCode == f).ToList();
                var facilityTransactions = transactions.Where(t => t.FacilityCode == f).ToList();

                return new MobileReportFacilitySummaryDto(
                    f,
                    FacilityName(f, facilityNames),
                    IsDailyReportFacility(f),
                    facilityTransactions.Sum(t => t.Amount),
                    facilityPayees.Sum(p => p.Balance),
                    facilityTransactions.Count,
                    facilityPayees.Count,
                    facilityPayees.Count(p => p.Status == PaymentStatus.Paid),
                    facilityPayees.Count(p => p.Status == PaymentStatus.Partial),
                    facilityPayees.Count(p => p.Status == PaymentStatus.Unpaid),
                    absentExcusedByFacility.GetValueOrDefault(f));
            })
            .OrderBy(f => f.FacilityCode)
            .ToList();

        var periodSummaries = dailyReportMode
            ? transactions.Select(t => t.PeriodDate)
                .Concat(absentExcusedByDate.Keys)
                .Distinct()
                .Select(date =>
                {
                    var dayTxns = transactions.Where(t => t.PeriodDate == date).ToList();
                    return new MobileReportPeriodSummaryDto(
                        date,
                        dayTxns.Sum(t => t.Amount),
                        dayTxns.Count,
                        dayTxns.Select(t => ReportPayeeKey(t.FacilityCode, t.StallNo, t.PayorName)).Distinct().Count(),
                        dayTxns.Count(t => t.IsPartial),
                        openItemsByDate.GetValueOrDefault(date),
                        absentExcusedByDate.GetValueOrDefault(date));
                })
                .OrderByDescending(p => p.PeriodDate)
                .ToList()
            : transactions.Count == 0
                ? []
                : [new MobileReportPeriodSummaryDto(
                    new DateOnly(year, month, 1),
                    transactions.Sum(t => t.Amount),
                    transactions.Count,
                    transactions.Select(t => ReportPayeeKey(t.FacilityCode, t.StallNo, t.PayorName)).Distinct().Count(),
                    transactions.Count(t => t.IsPartial),
                    payees.Count(p => p.Balance > 0m),
                    absentExcusedTotal)];

        var totals = new MobileReportTotalsDto(
            transactions.Sum(t => t.Amount),
            payees.Sum(p => p.Balance),
            transactions.Count,
            payees.Count,
            payees.Count(p => p.Status == PaymentStatus.Paid),
            payees.Count(p => p.Status == PaymentStatus.Partial),
            payees.Count(p => p.Status == PaymentStatus.Unpaid),
            absentExcusedTotal,
            selectedFacilities.Count);

        // ── Miscellaneous (electricity & water) summary for the reporting month — computed SEPARATELY
        //    from the collection totals above so the existing "Total Collected"/counts never change. ──
        MobileReportUtilitySummaryDto? utilitySummary = null;
        if (selectedSet.Contains(FacilityCode.NPM))
        {
            var ubills = await context.UtilityBills.AsNoTracking()
                .Where(b => b.BillingYear == year && b.BillingMonth == month
                         && (b.CollectorId == collectorId || b.CollectorId == null))
                .Select(b => new
                {
                    Payor = b.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                    b.Stall.StallNo,
                    b.ElecPreviousReading, b.ElecCurrentReading, b.ElecRatePerKwh, b.ElecStatus, b.ElecPartialAmount,
                    b.WaterPreviousReading, b.WaterCurrentReading, b.WaterRatePerCubicMeter, b.WaterStatus, b.WaterPartialAmount
                })
                .ToListAsync(cancellationToken);

            var utilityPayees = ubills.Select(b =>
            {
                var ec = Math.Max(0m, b.ElecCurrentReading - b.ElecPreviousReading) * b.ElecRatePerKwh;
                var wc = Math.Max(0m, b.WaterCurrentReading - b.WaterPreviousReading) * b.WaterRatePerCubicMeter;
                var ep = b.ElecStatus == PaymentStatus.Paid ? ec : b.ElecStatus == PaymentStatus.Partial ? b.ElecPartialAmount : 0m;
                var wp = b.WaterStatus == PaymentStatus.Paid ? wc : b.WaterStatus == PaymentStatus.Partial ? b.WaterPartialAmount : 0m;
                var total = ec + wc;
                var paid = ep + wp;
                var overall = b.ElecStatus == PaymentStatus.Paid && b.WaterStatus == PaymentStatus.Paid ? PaymentStatus.Paid
                            : paid <= 0m ? PaymentStatus.Unpaid : PaymentStatus.Partial;
                return new MobileReportUtilityPayeeDto(
                    string.IsNullOrWhiteSpace(b.Payor) ? "No active occupant" : b.Payor,
                    b.StallNo, b.ElecStatus.ToString(), b.WaterStatus.ToString(), overall.ToString(),
                    total, paid, total - paid);
            })
            .Where(r => r.TotalCharge > 0m)   // only bills that actually carry a charge
            .OrderBy(r => r.StallNo)
            .ToList();

            if (utilityPayees.Count > 0)
            {
                utilitySummary = new MobileReportUtilitySummaryDto(
                    utilityPayees.Sum(r => r.TotalCharge),
                    utilityPayees.Sum(r => r.AmountPaid),
                    utilityPayees.Sum(r => r.Balance),
                    utilityPayees.Count,
                    utilityPayees.Count(r => r.OverallStatus == nameof(PaymentStatus.Paid)),
                    utilityPayees.Count(r => r.OverallStatus == nameof(PaymentStatus.Partial)),
                    utilityPayees.Count(r => r.OverallStatus == nameof(PaymentStatus.Unpaid)),
                    utilityPayees);
            }
        }

        return new MobileCollectorReportDto(
            year,
            month,
            fromDate,
            toDate,
            dailyReportMode,
            totals,
            facilitySummaries,
            periodSummaries,
            payees
                .OrderBy(p => p.FacilityCode)
                .ThenBy(p => p.AreaLabel)
                .ThenBy(p => p.StallNo)
                .ThenBy(p => p.PayorName)
                .ToList(),
            transactions
                .OrderByDescending(t => t.PeriodDate)
                .ThenBy(t => t.FacilityCode)
                .ThenBy(t => t.StallNo)
                .Select(t => new MobileReportTransactionDto(
                    t.FacilityCode, t.FacilityName, t.StallNo, t.PayorName, t.PeriodDate, t.Amount, t.IsPartial, t.ORNumber))
                .ToList(),
            absentExcusedRows
                .OrderByDescending(a => a.Date)
                .ThenBy(a => a.FacilityCode)
                .ThenBy(a => a.StallNo)
                .ToList(),
            utilitySummary);
    }

    public async Task<CollectorUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await context.CollectorUsers
            .FirstOrDefaultAsync(c => c.Username == username, cancellationToken);
    }

    public async Task<MobileCollectorProfileDto?> GetCollectorProfileAsync(Guid collectorId, CancellationToken cancellationToken = default)
    {
        var collector = await context.CollectorUsers
            .AsNoTracking()
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c => c.Id == collectorId, cancellationToken);

        if (collector is null)
            return null;

        // ── Lifetime collected (recognized) across every facility type this collector handled ──
        var monthlyTotal = await context.PaymentRecords
            .Where(p => p.CollectorId == collectorId)
            .SumAsync(p => p.Status == PaymentStatus.Paid
                ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + ((p.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo)
                : p.Status == PaymentStatus.Partial ? p.PartialAmount : 0m, cancellationToken);
        var dailyTotal = await context.DailyCollections
            .Where(d => d.CollectorId == collectorId && d.IsPaid)
            .SumAsync(d => d.DailyFee + ((d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo), cancellationToken);
        var slaughterTotal = await context.SlaughterTransactions
            .Where(s => s.CollectorId == collectorId)
            .SumAsync(s => s.RatePerHead * s.NumberOfHeads, cancellationToken);
        var tripTotal = await context.TrmTrips
            .Where(t => t.CollectorId == collectorId)
            .SumAsync(t => t.Fee, cancellationToken);
        var tpmTotal = await context.TpmAttendances
            .Where(a => a.CollectorId == collectorId && a.IsPaid)
            .SumAsync(a => a.Fee, cancellationToken);
        var totalCollected = monthlyTotal + dailyTotal + slaughterTotal + tripTotal + tpmTotal;

        // ── Distinct active days (PH business dates) the collector recorded a collection ──
        var dates = new HashSet<DateOnly>();
        foreach (var d in await context.DailyCollections.Where(x => x.CollectorId == collectorId && x.IsPaid)
                     .Select(x => x.CollectionDate).Distinct().ToListAsync(cancellationToken))
            dates.Add(d);
        foreach (var d in await context.SlaughterTransactions.Where(x => x.CollectorId == collectorId)
                     .Select(x => x.TransactionDate).Distinct().ToListAsync(cancellationToken))
            dates.Add(d);
        foreach (var d in await context.TpmAttendances.Where(x => x.CollectorId == collectorId && x.IsPaid)
                     .Select(x => x.MarketDate).Distinct().ToListAsync(cancellationToken))
            dates.Add(d);
        foreach (var ts in await context.PaymentRecords.Where(x => x.CollectorId == collectorId && x.Status != PaymentStatus.Unpaid)
                     .Select(x => x.PaidAt ?? x.UpdatedAt ?? x.CreatedAt).ToListAsync(cancellationToken))
            dates.Add(DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(ts).Date));
        foreach (var ts in await context.TrmTrips.Where(x => x.CollectorId == collectorId)
                     .Select(x => x.RecordedAt).ToListAsync(cancellationToken))
            dates.Add(DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(ts).Date));

        return new MobileCollectorProfileDto(
            collector.FullName ?? "Collector",
            collector.EmployeeId ?? string.Empty,
            collector.ContactNumber ?? string.Empty,
            collector.Email ?? string.Empty,
            totalCollected,
            dates.Count,
            collector.FacilityAssignments.Count);
    }

    public async Task<CollectorUser?> GetByUsernameOrEmployeeIdAsync(string usernameOrEmployeeId, CancellationToken cancellationToken = default)
    {
        var normalized = usernameOrEmployeeId.Trim();
        return await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c =>
                c.Username == normalized || c.EmployeeId == normalized,
                cancellationToken);
    }

    public async Task<List<CollectorListDto>> GetAllCollectorsWithStatsAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var collectors = await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .ToListAsync(cancellationToken);

        var collectorIds = collectors.Select(c => c.Id).ToList();

        // Aggregate payment stats per collector in ONE query (was N queries in a loop).
        var paymentStats = await context.PaymentRecords
            .Where(p => p.CollectorId != null && collectorIds.Contains(p.CollectorId.Value)
                        && p.BillingYear == year && p.BillingMonth == month)
            .GroupBy(p => p.CollectorId!.Value)
            .Select(g => new
            {
                CollectorId = g.Key,
                Total = g.Sum(p => p.Status == PaymentStatus.Paid
                    ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + (p.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo
                    : p.Status == PaymentStatus.Partial ? p.PartialAmount : 0m),
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        // Aggregate daily-collection stats per collector in ONE query, keyed by the
        // CollectorId FK (the previous CreatedBy == Username join could never match).
        var dailyStats = await context.DailyCollections
            .Where(d => d.CollectorId != null && collectorIds.Contains(d.CollectorId.Value)
                        && d.CollectionDate.Year == year && d.CollectionDate.Month == month)
            .GroupBy(d => d.CollectorId!.Value)
            .Select(g => new
            {
                CollectorId = g.Key,
                Total = g.Sum(d => d.DailyFee + (d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo),
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        // SLH (per-head slaughter), TRM (per-trip), TPM (per-vendor Friday) collections also carry
        // CollectorId — without these, collectors assigned to those facilities show ₱0 / 0 here.
        var slaughterStats = await context.SlaughterTransactions
            .Where(s => s.CollectorId != null && collectorIds.Contains(s.CollectorId.Value)
                        && s.TransactionDate.Year == year && s.TransactionDate.Month == month)
            .GroupBy(s => s.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Total = g.Sum(s => s.RatePerHead * s.NumberOfHeads), Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var (trmStartUtc, trmEndUtc) = PhilippineTime.MonthUtcRange(year, month);
        var tripStats = await context.TrmTrips
            .Where(t => t.CollectorId != null && collectorIds.Contains(t.CollectorId.Value)
                        && t.RecordedAt >= trmStartUtc && t.RecordedAt < trmEndUtc)
            .GroupBy(t => t.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Total = g.Sum(t => t.Fee), Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var tpmStats = await context.TpmAttendances
            .Where(a => a.CollectorId != null && collectorIds.Contains(a.CollectorId.Value) && a.IsPaid
                        && a.MarketDate.Year == year && a.MarketDate.Month == month)
            .GroupBy(a => a.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Total = g.Sum(a => a.Fee), Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var result = new List<CollectorListDto>();

        foreach (var collector in collectors)
        {
            paymentStats.TryGetValue(collector.Id, out var payment);
            dailyStats.TryGetValue(collector.Id, out var daily);
            slaughterStats.TryGetValue(collector.Id, out var slaughter);
            tripStats.TryGetValue(collector.Id, out var trip);
            tpmStats.TryGetValue(collector.Id, out var tpm);

            result.Add(new CollectorListDto(
                collector.Id,
                collector.FullName!,
                collector.Email!,
                collector.EmployeeId!,
                collector.FacilityAssignments.Select(fa => fa.FacilityCode).ToList(),
                (payment?.Total ?? 0m) + (daily?.Total ?? 0m) + (slaughter?.Total ?? 0m) + (trip?.Total ?? 0m) + (tpm?.Total ?? 0m),
                (payment?.Count ?? 0) + (daily?.Count ?? 0) + (slaughter?.Count ?? 0) + (trip?.Count ?? 0) + (tpm?.Count ?? 0),
                collector.LastActiveAt,
                collector.IsActive));
        }

        return result.OrderByDescending(c => c.LastActiveAt).ToList();
    }

    public async Task<CollectorActivityDto?> GetCollectorActivityAsync(Guid collectorId, int year, int month, CancellationToken cancellationToken = default)
    {
        var collector = await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c => c.Id == collectorId, cancellationToken);

        if (collector is null)
            return null;

        var collectedThisMonth = await context.PaymentRecords
            .Where(p => p.CollectorId == collector.Id && 
                        p.BillingYear == year && 
                        p.BillingMonth == month)
            .SumAsync(p => p.Status == PaymentStatus.Paid
                          ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + ((p.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo)
                          : p.Status == PaymentStatus.Partial ? p.PartialAmount : 0m, cancellationToken) +
            await context.DailyCollections
            .Where(d => d.CollectorId == collector.Id && 
                        d.CollectionDate.Year == year && 
                        d.CollectionDate.Month == month)
            .SumAsync(d => d.DailyFee + ((d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo), cancellationToken);

        var (mStartUtc, mEndUtc) = PhilippineTime.MonthUtcRange(year, month);

        collectedThisMonth +=
            await context.SlaughterTransactions
                .Where(s => s.CollectorId == collector.Id && s.TransactionDate.Year == year && s.TransactionDate.Month == month)
                .SumAsync(s => s.RatePerHead * s.NumberOfHeads, cancellationToken) +
            await context.TrmTrips
                .Where(t => t.CollectorId == collector.Id && t.RecordedAt >= mStartUtc && t.RecordedAt < mEndUtc)
                .SumAsync(t => t.Fee, cancellationToken) +
            await context.TpmAttendances
                .Where(a => a.CollectorId == collector.Id && a.IsPaid && a.MarketDate.Year == year && a.MarketDate.Month == month)
                .SumAsync(a => a.Fee, cancellationToken);

        var transactions = await context.PaymentRecords
            .CountAsync(p => p.CollectorId == collector.Id && 
                            p.BillingYear == year && 
                            p.BillingMonth == month, cancellationToken) +
            await context.DailyCollections
            .CountAsync(d => d.CollectorId == collector.Id && 
                            d.CollectionDate.Year == year && 
                            d.CollectionDate.Month == month, cancellationToken) +
            await context.SlaughterTransactions
            .CountAsync(s => s.CollectorId == collector.Id && s.TransactionDate.Year == year && s.TransactionDate.Month == month, cancellationToken) +
            await context.TrmTrips
            .CountAsync(t => t.CollectorId == collector.Id && t.RecordedAt >= mStartUtc && t.RecordedAt < mEndUtc, cancellationToken) +
            await context.TpmAttendances
            .CountAsync(a => a.CollectorId == collector.Id && a.IsPaid && a.MarketDate.Year == year && a.MarketDate.Month == month, cancellationToken);

        var recentPayments = await context.PaymentRecords
            .Where(p => p.CollectorId == collector.Id && p.Status != PaymentStatus.Unpaid)
            .OrderByDescending(p => p.PaidAt ?? p.UpdatedAt)
            .Take(10)
            .Select(p => new RecentTransactionDto(
                p.ORNumber ?? "—",
                p.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault() ?? "—",
                p.Stall.Facility!.Code,
                "Stall Rental",
                p.Status == PaymentStatus.Paid
                    ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + ((p.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo)
                    : p.Status == PaymentStatus.Partial ? p.PartialAmount : 0m,
                p.Status.ToString(),
                p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt))
            .ToListAsync(cancellationToken);

        // NPM collectors record daily collections (not monthly PaymentRecords), so these must be
        // merged in or the Recent Transactions list would be empty for them.
        var recentDaily = await context.DailyCollections
            .Where(d => d.CollectorId == collector.Id && d.IsPaid)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Take(10)
            .Select(d => new RecentTransactionDto(
                d.ORNumber ?? "—",
                d.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault() ?? "—",
                d.Stall.Facility!.Code,
                "Daily Fee",
                d.DailyFee + ((d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo),
                "Paid",
                d.UpdatedAt ?? d.CreatedAt))
            .ToListAsync(cancellationToken);

        // Per-transaction facilities (SLH/TRM/TPM) — these never produce PaymentRecords or
        // DailyCollections, so their recorded activity must be merged in explicitly.
        var recentSlaughter = await context.SlaughterTransactions
            .Where(s => s.CollectorId == collector.Id)
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Take(10)
            .Select(s => new RecentTransactionDto(
                s.ORNumber ?? "—",
                s.OwnerName,
                FacilityCode.SLH,
                "Slaughter",
                s.RatePerHead * s.NumberOfHeads,
                "Paid",
                s.UpdatedAt ?? s.CreatedAt))
            .ToListAsync(cancellationToken);

        var recentTrips = await context.TrmTrips
            .Where(t => t.CollectorId == collector.Id)
            .OrderByDescending(t => t.RecordedAt)
            .Take(10)
            .Select(t => new RecentTransactionDto(
                t.ORNumber ?? "—",
                t.DriverName,
                FacilityCode.TRM,
                "Terminal Trip",
                t.Fee,
                "Paid",
                t.RecordedAt))
            .ToListAsync(cancellationToken);

        var recentTpm = await context.TpmAttendances
            .Where(a => a.CollectorId == collector.Id && a.IsPaid)
            .OrderByDescending(a => a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt)
            .Take(10)
            .Select(a => new RecentTransactionDto(
                a.ORNumber ?? "—",
                a.Vendor!.VendorName,
                FacilityCode.TPM,
                "Market Day",
                a.Fee,
                "Paid",
                a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt))
            .ToListAsync(cancellationToken);

        var recentTransactions = recentPayments
            .Concat(recentDaily)
            .Concat(recentSlaughter)
            .Concat(recentTrips)
            .Concat(recentTpm)
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .ToList();

        return new CollectorActivityDto(
            collector.Id,
            collector.FullName!,
            collector.EmployeeId!,
            collector.Email!,
            collector.ContactNumber!,
            collector.FacilityAssignments.Select(fa => fa.FacilityCode).ToList(),
            collectedThisMonth,
            transactions,
            collector.FacilityAssignments.Count,
            collector.LastActiveAt,
            recentTransactions);
    }

    public async Task AddAsync(CollectorUser collector, CancellationToken cancellationToken = default)
    {
        await context.CollectorUsers.AddAsync(collector, cancellationToken);
    }

    public async Task<bool> IsEmployeeIdUniqueAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        // Uniqueness must consider soft-deleted users too (their rows still exist), so bypass the global filter.
        return !await context.CollectorUsers.IgnoreQueryFilters().AnyAsync(c => c.EmployeeId == employeeId, cancellationToken);
    }

    public async Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default)
    {
        return !await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == username, cancellationToken);
    }

    public async Task<bool> IsEmailUniqueAsync(string email, CancellationToken cancellationToken = default)
    {
        return !await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken);
    }

    public async Task AddFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default)
    {
        var facilities = await context.Facilities
            .Where(f => facilityCodes.Contains(f.Code))
            .ToListAsync(cancellationToken);

        foreach (var facility in facilities)
        {
            var assignment = CollectorFacilityAssignment.Create(
                collectorId,
                facility.Id,
                facility.Code);

            await context.CollectorFacilityAssignments.AddAsync(assignment, cancellationToken);
        }
    }

    public async Task ReplaceFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default)
    {
        var existing = await context.CollectorFacilityAssignments
            .Where(a => a.CollectorId == collectorId)
            .ToListAsync(cancellationToken);

        // Diff so unchanged assignments are left intact (avoids unique-index conflicts on re-add).
        context.CollectorFacilityAssignments.RemoveRange(existing.Where(a => !facilityCodes.Contains(a.FacilityCode)));

        var existingCodes = existing.Select(a => a.FacilityCode).ToHashSet();
        var toAdd = facilityCodes.Where(c => !existingCodes.Contains(c)).ToList();
        await AddFacilityAssignmentsAsync(collectorId, toAdd, cancellationToken);
    }

    public async Task<string> GenerateNextEmployeeIdAsync(CancellationToken cancellationToken = default)
    {
        var currentYear = PhilippineTime.Now.Year;
        var prefix = $"EEMO-{currentYear}-";

        var lastEmployeeId = await context.CollectorUsers
            .IgnoreQueryFilters()
            .Where(c => c.EmployeeId!.StartsWith(prefix))
            .OrderByDescending(c => c.EmployeeId)
            .Select(c => c.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        int nextNumber = 1;
        if (lastEmployeeId != null)
        {
            var numberPart = lastEmployeeId.Replace(prefix, "");
            if (int.TryParse(numberPart, out int lastNumber))
            {
                nextNumber = lastNumber + 1;
            }
        }

        return $"{prefix}{nextNumber:D3}";
    }

    private static bool IsDailyReportFacility(FacilityCode facility) =>
        facility is FacilityCode.NPM or FacilityCode.SLH or FacilityCode.TRM or FacilityCode.TPM;

    private static bool IsMonthlyRentalFacility(FacilityCode facility) =>
        facility is FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE;

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

    private static decimal RecognizedNpmPaymentRevenue(PaymentRecord payment, DateOnly startDate, DateOnly endDate, Stall stall)
    {
        if (payment.Status == PaymentStatus.Unpaid || !IsPaymentInDateRange(payment.BillingYear, payment.BillingMonth, startDate, endDate))
            return 0m;

        var dailyRevenue = RecognizedNpmDailyFeeRevenue(payment, startDate, endDate, stall);
        if (!IsWholeBillingMonthSelected(payment, startDate, endDate) || payment.Status != PaymentStatus.Paid)
            return dailyRevenue;

        return dailyRevenue + payment.FishKilos.GetValueOrDefault() * FeeRates.NpmFishFeePerKilo;
    }

    private static decimal RecognizedNpmDailyFeeRevenue(PaymentRecord payment, DateOnly startDate, DateOnly endDate, Stall stall)
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

    private static bool NpmPaymentCoversDate(PaymentRecord payment, DateOnly date, Stall stall) =>
        RecognizedNpmDailyFeeRevenue(payment, date, date, stall) > 0m;

    private static decimal AllocatePrepaidDailyAmountToCollectableRange(
        decimal prepaidAmount,
        Stall stall,
        DateOnly monthStart,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        if (prepaidAmount <= 0m || FeeRates.NpmDailyFee <= 0m || rangeEnd < rangeStart)
            return 0m;

        var monthEnd = new DateOnly(monthStart.Year, monthStart.Month, DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
        var collectableDays = new List<DateOnly>();
        for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
        {
            if (IsStallCollectableOn(stall, date))
                collectableDays.Add(date);
        }

        var fullCoveredDays = (int)Math.Floor(prepaidAmount / FeeRates.NpmDailyFee);
        var remainder = prepaidAmount % FeeRates.NpmDailyFee;
        var amount = collectableDays
            .Take(fullCoveredDays)
            .Where(d => d >= rangeStart && d <= rangeEnd)
            .Sum(_ => FeeRates.NpmDailyFee);

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

        return stall.Section.HasValue ? GetSectionName(stall.Section) : string.Empty;
    }

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
        string? ORNumber);
}
