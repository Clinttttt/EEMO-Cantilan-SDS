using EEMOCantilanSDS.Application.Common.Interface.Time;
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
    ITenantContext tenantContext, IClock clock)
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
                "import, where one row is one month's payment.", ResultStatus.Invalid);
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

        var today = clock.PhilippineToday;
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

            // A receipt is required for every day recorded, but it need not be stated on the ROW.
            //
            // The market issues a receipt per collection, so an office's sheet often carries one against each day and
            // none for the month. Demanding a row-level OR rejected exactly that sheet - the whole row, with every one
            // of its receipts - and asked for a figure the sheet does not keep. What matters is that no DAY is written
            // without a receipt, which is checked as each day is settled.
            var rowOr = (row.OrNumber ?? string.Empty).Trim();
            var statedDays = row.Days ?? Array.Empty<ImportDailyDay>();

            if (rowOr.Length == 0 && statedDays.Count == 0)
            {
                // Names both ways the office can satisfy this, because a row reaching here has neither: no receipt for
                // the month, and no dated day carrying one of its own. The old wording said only "an OR number is
                // required", which read as a contradiction to an office that had written a receipt against every line
                // it listed - those lines had no DATES, so none of them arrived.
                Reject("No OR number for this month, and no dated day carrying one. Write a receipt against the " +
                       "month, or date each day and write its own.");
                continue;
            }

            if (!stallsByNo.TryGetValue(stallNo, out var stall))
            {
                Reject($"No stall {stallNo} in this facility{(request.Section.HasValue ? " and section" : "")}. " +
                       "Import the stallholders first, or correct the number.");
                continue;
            }

            // The count and the dates have to agree. A row claiming two days and naming five contradicts itself, and
            // honouring the dates settled all five while reporting "recorded in full" against a claim of two - more
            // money than the row claimed, described as complete. Refused rather than reconciled to one of the two, for
            // the same reason a count above 31 is refused: the office wrote both figures, and only it can say which is
            // right.
            var datedCount = (row.Days ?? Array.Empty<ImportDailyDay>())
                .Select(d => d.Date)
                .Distinct()
                .Count();

            if (datedCount > row.DaysPaid)
            {
                Reject($"{row.DaysPaid} days paid, but {datedCount} days are dated. Correct one or the other.");
                continue;
            }

            var orNumber = rowOr;

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
            var refusedDates = new List<string>();

            // Whether the office stated the days, or only how many. Stated days are honoured exactly - see the DTO.
            // De-duplicated by DATE: the same day twice is one day's collection, whatever receipts are written beside
            // it, and the first receipt given for a day is the one recorded.
            var stated = (row.Days ?? Array.Empty<ImportDailyDay>())
                .GroupBy(d => d.Date)
                .Select(g => g.First())
                .OrderBy(d => d.Date)
                .ToList();

            // Settles one day, or says why it could not. Shared so a stated date and a filled one are judged by
            // exactly the same rules - a date the office named must not slip past a guard a filled day would meet.
            async Task<string?> TrySettle(DateOnly date, string? dayOr)
            {
                if (date.Year != row.BillingYear || date.Month != row.BillingMonth)
                    return $"{date:yyyy-MM-dd} is not in {period}";
                if (date > today) return $"{date:yyyy-MM-dd} has not happened yet";
                if (closures.Contains(date)) return $"{date:yyyy-MM-dd} was a market closure";
                if (alreadyThisBatch.Contains(date)) return $"{date:yyyy-MM-dd} is already claimed by an earlier row";
                if (!occupancies.Any(o => o.Start <= date && date <= o.BillableEnd))
                    return $"{date:yyyy-MM-dd} is outside the term";

                if (onRecord.TryGetValue(date, out var already) && (already.IsPaid || already.IsAbsent))
                    return $"{date:yyyy-MM-dd} is already {(already.IsAbsent ? "excused" : "collected")}";

                // The day's own receipt where the sheet gives one, the month's otherwise. A day with neither is refused
                // rather than written: a collection with no OR is reported as missing a receipt by every arrears list
                // that reads it, and one written here would raise that alarm for ever.
                var receipt = string.IsNullOrWhiteSpace(dayOr) ? orNumber : dayOr!.Trim();
                if (receipt.Length == 0)
                    return $"{date:yyyy-MM-dd} has no OR number, on the day or on the month";

                var fee = stall.ResolveDailyFee(snapshot.Resolve(FeeRateKey.NpmDailyStall, date));

                if (already is null)
                {
                    already = DailyCollection.Create(stall.Id, date, Actor, fee);
                    already.MarkPaid(orNumber: string.Empty, collectorId: null, fishKilos: null, updatedBy: Actor);
                    await dailyRepo.AddAsync(already, ct);
                    onRecord[date] = already;
                }
                else
                {
                    already.MarkPaid(orNumber: string.Empty, collectorId: null, fishKilos: null, updatedBy: Actor);
                }

                already.SetOrNumber(receipt, Actor);
                alreadyThisBatch.Add(date);
                settled.Add(already);
                rowAmount += already.TotalCollected;
                return null;
            }

            if (stated.Count > 0)
            {
                foreach (var day in stated)
                {
                    if (await TrySettle(day.Date, day.OrNumber) is { } why) refusedDates.Add(why);
                }
            }
            else
            {
                // Only a count. Filled in order, earliest first, which is the most defensible reading of a sheet that
                // does not say which days.
                for (var day = 1; day <= daysInMonth && settled.Count < row.DaysPaid; day++)
                    await TrySettle(new DateOnly(row.BillingYear, row.BillingMonth, day), null);
            }

            if (settled.Count == 0)
            {
                results.Add(new BulkImportDailyRowResult(
                    row.RowNumber, stallNo, period, row.DaysPaid, 0, 0m, ImportDailyOutcome.AlreadyRecorded,
                    refusedDates.Count > 0
                        ? $"None of the days given could be settled: {string.Join("; ", refusedDates)}."
                        : "No day in that month was left to settle: its days are already collected, excused, closed, " +
                          "or outside the term."));
                continue;
            }

            touchedMonths.Add((row.BillingYear, row.BillingMonth));
            totalDays += settled.Count;
            totalAmount += rowAmount;

            var outcome = settled.Count >= row.DaysPaid
                ? ImportDailyOutcome.RecordedInFull
                : ImportDailyOutcome.RecordedInPart;

            // A stated date that could not be settled is NAMED. A count that could not be filled says how far it got.
            // Either way the office is told what happened rather than handed a total to trust.
            var note = outcome == ImportDailyOutcome.RecordedInPart || refusedDates.Count > 0
                ? refusedDates.Count > 0
                    ? $"{settled.Count} of {row.DaysPaid} days recorded. {string.Join("; ", refusedDates)}."
                    : $"Only {settled.Count} of {row.DaysPaid} days could be settled: the rest were already " +
                      "collected, excused, closed, or outside the term."
                : null;

            results.Add(new BulkImportDailyRowResult(
                row.RowNumber, stallNo, period, row.DaysPaid, settled.Count, rowAmount, outcome, note));
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
