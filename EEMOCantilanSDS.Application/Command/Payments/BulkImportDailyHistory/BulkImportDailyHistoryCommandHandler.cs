using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.BulkImportDailyHistory;

/// <summary>
/// Writes an office's market collection history as ordinary daily collections.
///
/// <para>Nothing new is invented: these are the same DailyCollection rows the collector's app writes, so every report,
/// balance and roster that already reads them works untouched. The only thing this handler decides is WHICH days a
/// count of days refers to, and it decides it the same way the market's own collection dialog does.</para>
/// </summary>
public class BulkImportDailyHistoryCommandHandler(
    IStallRepository stallRepo,
    IDailyCollectionRepository dailyRepo,
    IPaymentRepository paymentRepo,
    INpmMarketClosureRepository closureRepo,
    IFacilityRepository facilityRepo,
    IFeeRateResolver feeRateResolver,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext)
    : IRequestHandler<BulkImportDailyHistoryCommand, Result<BulkImportDailyResultDto>>
{
    /// <summary>
    /// Stamped as the actor on every row, so the audit trail distinguishes history the office brought in from money a
    /// collector took in the field. The same marker the monthly import uses.
    /// </summary>
    private const string Actor = "HistoryImport";

    /// <summary>
    /// A month cannot hold more days than its calendar. Rejected rather than clamped: a count above this is a
    /// transcription error, and silently settling 31 of a claimed 45 would report success on a wrong figure.
    /// </summary>
    private const int MaxDaysInAnyMonth = 31;

    public async Task<Result<BulkImportDailyResultDto>> Handle(
        BulkImportDailyHistoryCommand request, CancellationToken ct)
    {
        var facility = await facilityRepo.GetByCodeAsync(request.FacilityCode, ct);
        if (facility is null)
            return Result<BulkImportDailyResultDto>.NotFound();

        // Asked of the facility, not of a list of codes, so an LGU that maps its own facilities to daily billing is
        // served without new code.
        if (facility.Archetype != BillingArchetype.DailyStall)
        {
            return Result<BulkImportDailyResultDto>.Failure(
                $"{facility.Name} is not collected per market day. A facility billed by the month has its own " +
                "import, where one row is one month's payment.", 400);
        }

        var stalls = await stallRepo.GetStallsWithContractsByFacilityAsync(
            request.FacilityCode, request.Section, request.CustomSectionName, ct);

        // Matched on the number WITHIN the facility and section, never on the number alone: the market has three
        // spaces called "1", and a facility-and-number key is the mistake this codebase has had to correct
        // repeatedly.
        var stallsByNo = new Dictionary<string, Stall>(StringComparer.OrdinalIgnoreCase);
        foreach (var stall in stalls)
        {
            var no = (stall.StallNo ?? string.Empty).Trim();
            if (no.Length > 0) stallsByNo[no] = stall;
        }

        var today = PhilippineTime.Today;
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);

        var results = new List<BulkImportDailyRowResult>(request.Rows.Count);
        var touchedMonths = new HashSet<(int Year, int Month)>();

        // Days this batch has already settled, so two rows naming the same space and month cannot both claim the
        // same day. The repository is read once per space and month; without this the second row would re-read stale
        // state and double-count.
        var settledInBatch = new Dictionary<string, HashSet<DateOnly>>(StringComparer.OrdinalIgnoreCase);

        var totalDays = 0;
        var totalAmount = 0m;

        foreach (var row in request.Rows)
        {
            var stallNo = (row.StallNo ?? string.Empty).Trim();
            var period = $"{row.BillingYear:0000}-{row.BillingMonth:00}";

            void Reject(string reason) =>
                results.Add(new BulkImportDailyRowResult(
                    row.RowNumber, stallNo, period, row.DaysPaid, 0, 0m, ImportDailyOutcome.Rejected, reason));

            if (stallNo.Length == 0) { Reject("Stall / Space No. is required."); continue; }
            if (row.BillingMonth is < 1 or > 12) { Reject("The month must be between 1 and 12."); continue; }
            if (row.BillingYear is < 1990 or > 2200) { Reject("The year is not a real year."); continue; }
            if (row.DaysPaid <= 0) { Reject("Days paid must be more than zero."); continue; }
            if (row.DaysPaid > MaxDaysInAnyMonth)
            {
                Reject($"{row.DaysPaid} days is more than any month holds.");
                continue;
            }

            // A history is a record of days already collected, so a month that has not started cannot have any. The
            // month in progress is allowed: its days up to today are real collection days.
            if (row.BillingYear > today.Year || (row.BillingYear == today.Year && row.BillingMonth > today.Month))
            {
                Reject($"{period} has not started yet. A collection history can only cover months up to " +
                       $"{today.Year:0000}-{today.Month:00}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.OrNumber))
            {
                Reject("An OR number is required: collections without one are reported as missing a receipt.");
                continue;
            }

            if (!stallsByNo.TryGetValue(stallNo, out var stall))
            {
                Reject($"No stall {stallNo} in this facility{(request.Section.HasValue ? " and section" : "")}. " +
                       "Import the stallholders first, or correct the number.");
                continue;
            }

            var orNumber = row.OrNumber!.Trim();

            // The days nobody owes for. Every exclusion here matches the market's own collection dialog, so an
            // imported month and a collected month agree about what the month contained.
            var closures = new HashSet<DateOnly>(
                (await closureRepo.GetByMonthAsync(row.BillingYear, row.BillingMonth, ct)).Select(c => c.ClosureDate));

            var onRecord = new Dictionary<DateOnly, DailyCollection>();
            foreach (var dc in await dailyRepo.GetByStallAndMonthAsync(stall.Id, row.BillingYear, row.BillingMonth, ct))
                onRecord[dc.CollectionDate] = dc;

            var batchKey = $"{stall.Id}|{period}";
            if (!settledInBatch.TryGetValue(batchKey, out var alreadyThisBatch))
                settledInBatch[batchKey] = alreadyThisBatch = new HashSet<DateOnly>();

            // A day is settleable when SOME term answers for it, not only the current one - otherwise days left
            // behind by a lessee who has gone could never be collected.
            var occupancies = stall.Occupancies(today);

            var daysInMonth = DateTime.DaysInMonth(row.BillingYear, row.BillingMonth);
            var settled = new List<DailyCollection>();
            var rowAmount = 0m;

            for (var day = 1; day <= daysInMonth && settled.Count < row.DaysPaid; day++)
            {
                var date = new DateOnly(row.BillingYear, row.BillingMonth, day);

                if (date > today) break;                                  // never settle a day that has not happened
                if (closures.Contains(date)) continue;                    // facility-wide closure: nothing owed
                if (alreadyThisBatch.Contains(date)) continue;            // claimed by an earlier row in this file
                if (!occupancies.Any(o => o.Start <= date && date <= o.BillableEnd)) continue;

                if (onRecord.TryGetValue(date, out var existing) && (existing.IsPaid || existing.IsAbsent))
                    continue;                                             // already collected, or excused

                var fee = stall.ResolveDailyFee(snapshot.Resolve(FeeRateKey.NpmDailyStall, date));

                if (existing is null)
                {
                    existing = DailyCollection.Create(stall.Id, date, Actor, fee);
                    existing.MarkPaid(orNumber: string.Empty, collectorId: null, fishKilos: null, updatedBy: Actor);
                    await dailyRepo.AddAsync(existing, ct);
                    onRecord[date] = existing;
                }
                else
                {
                    existing.MarkPaid(orNumber: string.Empty, collectorId: null, fishKilos: null, updatedBy: Actor);
                }

                existing.SetOrNumber(orNumber, Actor);
                alreadyThisBatch.Add(date);
                settled.Add(existing);
                rowAmount += existing.TotalCollected;
            }

            if (settled.Count == 0)
            {
                results.Add(new BulkImportDailyRowResult(
                    row.RowNumber, stallNo, period, row.DaysPaid, 0, 0m, ImportDailyOutcome.AlreadyRecorded,
                    "No day in that month was left to settle: its days are already collected, excused, closed, or " +
                    "outside the term."));
                continue;
            }

            touchedMonths.Add((row.BillingYear, row.BillingMonth));
            totalDays += settled.Count;
            totalAmount += rowAmount;

            var outcome = settled.Count >= row.DaysPaid
                ? ImportDailyOutcome.RecordedInFull
                : ImportDailyOutcome.RecordedInPart;

            results.Add(new BulkImportDailyRowResult(
                row.RowNumber, stallNo, period, row.DaysPaid, settled.Count, rowAmount, outcome,
                outcome == ImportDailyOutcome.RecordedInPart
                    ? $"Only {settled.Count} of {row.DaysPaid} days could be settled: the rest were already " +
                      "collected, excused, closed, or outside the term."
                    : null));
        }

        if (results.Any(r => r.Outcome != ImportDailyOutcome.Rejected))
        {
            await unitOfWork.SaveChangesAsync(ct);

            foreach (var (year, month) in touchedMonths)
            {
                await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                    tenantContext.TenantCode, request.FacilityCode, year, month, ct);
            }
        }

        return Result<BulkImportDailyResultDto>.Success(new BulkImportDailyResultDto(
            TotalRows: request.Rows.Count,
            RecordedCount: results.Count(r => r.Outcome == ImportDailyOutcome.RecordedInFull),
            PartialCount: results.Count(r => r.Outcome == ImportDailyOutcome.RecordedInPart),
            AlreadyRecordedCount: results.Count(r => r.Outcome == ImportDailyOutcome.AlreadyRecorded),
            RejectedCount: results.Count(r => r.Outcome == ImportDailyOutcome.Rejected),
            TotalDaysSettled: totalDays,
            TotalRecorded: totalAmount,
            Results: results));
    }
}
