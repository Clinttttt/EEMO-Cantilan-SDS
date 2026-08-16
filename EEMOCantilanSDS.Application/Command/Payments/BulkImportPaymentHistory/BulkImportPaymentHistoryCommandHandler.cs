using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.BulkImportPaymentHistory;

/// <inheritdoc cref="BulkImportPaymentHistoryCommand"/>
public class BulkImportPaymentHistoryCommandHandler(
    IStallRepository stallRepo,
    IPaymentRepository paymentRepo,
    IFacilityRepository facilityRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext, IClock clock) : IRequestHandler<BulkImportPaymentHistoryCommand, Result<BulkImportPaymentResultDto>>
{
    private const string Actor = "HistoryImport";

    /// <summary>Guards a fat-fingered figure without rejecting any real market rent.</summary>
    private const decimal MaxAmount = 10_000_000m;

    public async Task<Result<BulkImportPaymentResultDto>> Handle(
        BulkImportPaymentHistoryCommand request, CancellationToken ct)
    {
        var facility = await facilityRepo.GetByCodeAsync(request.FacilityCode, ct);
        if (facility is null)
            return Result<BulkImportPaymentResultDto>.NotFound();

        // Whether the facility bills by the month, asked of the facility itself rather than of a list of codes.
        //
        // The list used to be spelled out as TCC/NCC/BBQ/ICE, which quietly excluded every facility a Head adds for
        // their own LGU - those are monthly-rental too, and reuse this same machinery, so there was never a reason
        // beyond the list for them to be refused. The archetype is the field that says how a facility bills, and it
        // is per-tenant data, so an LGU that maps its facilities differently is answered correctly without new code.
        if (facility.Archetype != BillingArchetype.MonthlyRental)
        {
            return Result<BulkImportPaymentResultDto>.Failure(
                $"{facility.Name} is not billed by the month, so its history cannot be imported one month at a time. " +
                "The market is collected per market day and has its own import.", ResultStatus.Invalid);
        }

        var stalls = await stallRepo.GetStallsWithContractsByFacilityAsync(
            request.FacilityCode, request.Section, request.CustomSectionName, ct);

        // Matched on the number WITHIN the facility and section, never on the number alone: the market has three
        // spaces called "1", and a facility-and-number key is the mistake this codebase has had to correct
        // repeatedly.
        //
        // GROUPED rather than keyed, so a number that two spaces in the same section share is DETECTED. It used to be a
        // dictionary assignment, where the second space silently replaced the first — which lessee's account a row's money
        // landed on then depended on the order the repository returned them in. Such a row is refused below.
        var stallsByNo = stalls
            .Where(s => !string.IsNullOrWhiteSpace(s.StallNo))
            .GroupBy(s => s.StallNo!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var results = new List<BulkImportPaymentRowResult>(request.Rows.Count);
        var created = new List<PaymentRecord>();

        // Periods claimed by this batch, so a file that lists the same space and month twice cannot write two
        // payments for one month. The database is checked as well; this catches the duplicate inside one upload.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in request.Rows)
        {
            var stallNo = (row.StallNo ?? string.Empty).Trim();
            var period = $"{row.BillingYear:0000}-{row.BillingMonth:00}";

            void Reject(string reason) =>
                results.Add(new BulkImportPaymentRowResult(
                    row.RowNumber, stallNo, period, row.AmountPaid, ImportPaymentOutcome.Rejected, reason));

            if (stallNo.Length == 0) { Reject("Stall / Space No. is required."); continue; }
            if (row.BillingMonth is < 1 or > 12) { Reject("The billing month must be between 1 and 12."); continue; }
            if (row.BillingYear is < 1990 or > 2200) { Reject("The billing year is not a real year."); continue; }

            // A history is a record of money the office has already received, so a month that has not started yet
            // cannot have one. Left unchecked the row settles rent nobody has been billed for: the month arrives
            // already paid or part-paid, the vendor's own screens still show it as "Soon", and the year's collected
            // total carries money against a period the office's books have nothing to reconcile it to. The current
            // month is allowed - monthly rent falls due when the month opens, so it can genuinely be paid today.
            var thisMonth = clock.PhilippineToday;
            if (row.BillingYear > thisMonth.Year ||
                (row.BillingYear == thisMonth.Year && row.BillingMonth > thisMonth.Month))
            {
                Reject($"{period} has not started yet. A payment history can only cover months up to " +
                       $"{thisMonth.Year:0000}-{thisMonth.Month:00}.");
                continue;
            }
            if (row.AmountPaid <= 0m) { Reject("The amount paid must be more than zero."); continue; }
            if (row.AmountPaid > MaxAmount) { Reject("The amount paid is implausibly large."); continue; }
            if (string.IsNullOrWhiteSpace(row.OrNumber))
            {
                // Every real payment carries a receipt, and the arrears lists treat one without as needing
                // follow-up - so importing history without it would raise a false alarm on every row.
                Reject("An OR number is required: a recorded payment without one is reported as missing a receipt.");
                continue;
            }

            if (!stallsByNo.TryGetValue(stallNo, out var matches))
            {
                Reject($"No stall {stallNo} in this facility{(request.Section.HasValue ? " and section" : "")}. " +
                       "Import the stallholders first.");
                continue;
            }

            // Two spaces in the same section carrying the same number: refused, on the office's instruction. Recording the
            // payment against either would put it on an account the office did not name, and the row would be reported as
            // done. The office knows which space it meant; nothing here does.
            if (matches.Count > 1)
            {
                Reject($"{matches.Count} spaces in this facility{(request.Section.HasValue ? " and section" : "")} are " +
                       $"numbered {stallNo}, so this row does not say which one paid. Give the duplicates distinct numbers, " +
                       "then import again.");
                continue;
            }

            var stall = matches[0];

            // The occupancy ANSWERING for the month being paid for, not merely a contract whose term spans it.
            //
            // BillsCalendarMonth only asks whether a month falls inside a term. It does not know that the occupancy
            // was terminated early, handed to the next lessee, or ended by the stall being closed - so on a re-let
            // stall it could match the wrong lessee, take the rent from their contract, and then judge whether the
            // amount settled the month against a figure belonging to somebody else. StallOccupancy.AnsweringForMonth
            // is the rule of record that the register, the reports and the collection dialog all read; the import
            // must answer the same question the same way or it will disagree with every screen that shows it.
            var occupancy = StallOccupancy.AnsweringForMonth(
                stall.Occupancies(clock.PhilippineToday), row.BillingYear, row.BillingMonth);

            if (occupancy is null || !occupancy.Contract.BillsCalendarMonth(row.BillingYear, row.BillingMonth))
            {
                Reject($"No occupancy on stall {stallNo} answers for {period}. The month is before the term started, " +
                       "after it ended, or the space had been handed over or closed by then.");
                continue;
            }

            var contract = occupancy.Contract;

            var monthlyRent = contract.ActualMonthlyRental is > 0m
                ? contract.ActualMonthlyRental.Value
                : contract.MonthlyRentalRate;

            if (monthlyRent <= 0m)
            {
                Reject($"The contract covering {period} has no rent on record, so nothing can be settled against it.");
                continue;
            }

            if (!claimed.Add($"{stall.Id}|{period}"))
            {
                results.Add(new BulkImportPaymentRowResult(
                    row.RowNumber, stallNo, period, row.AmountPaid, ImportPaymentOutcome.AlreadyRecorded,
                    "This space and month appear more than once in this file; only the first row was recorded."));
                continue;
            }

            var existing = await paymentRepo.GetPaymentRecordAsync(stall.Id, row.BillingYear, row.BillingMonth, ct);
            if (existing is not null)
            {
                // Re-importing the same history must not double the month. Reported rather than skipped silently,
                // so the office can see the file has already been through.
                results.Add(new BulkImportPaymentRowResult(
                    row.RowNumber, stallNo, period, row.AmountPaid, ImportPaymentOutcome.AlreadyRecorded,
                    $"A payment for {period} is already recorded on this space."));
                continue;
            }

            // Short of the rent means the month stays outstanding for the remainder. Recording every row as settled
            // would repeat, across an entire history in one click, the defect that once let a single day's fee mark
            // a whole month paid.
            var isFull = row.AmountPaid >= monthlyRent;
            var record = PaymentRecord.Create(stall.Id, row.BillingYear, row.BillingMonth, monthlyRent, Actor);
            record.RecordPayment(
                orNumber: row.OrNumber!.Trim(),
                collectorId: Guid.Empty,          // no collector: this is the office's own historical record
                status: isFull ? PaymentStatus.Paid : PaymentStatus.Partial,
                partialAmount: isFull ? null : row.AmountPaid,
                remarks: BuildRemark(row),
                updatedBy: Actor);

            // No collector: this is the office's own historical record, not a collection taken in the system. The
            // column is nullable and the codebase's attribution rule writes null where nobody collected - a zero GUID
            // is not "nobody", and the transaction feed renders it as though a collector named it.
            record.ClearCollectorForImportedHistory();

            if (row.DatePaid is { } receivedOn)
            {
                // Dated from the office's books rather than the moment of import, or every row would appear in the
                // transaction feed and the dashboard's recent collections as collected today.
                record.BackdateReceipt(receivedOn.ToUniversalTime(), Actor);
            }

            created.Add(record);
            results.Add(new BulkImportPaymentRowResult(
                row.RowNumber, stallNo, period, row.AmountPaid,
                isFull ? ImportPaymentOutcome.RecordedPaid : ImportPaymentOutcome.RecordedPartial,
                null));
        }

        if (created.Count > 0)
        {
            foreach (var record in created)
                await paymentRepo.AddAsync(record, ct);

            // One transaction for the batch, as with the stallholder import: a database failure leaves the ledger
            // exactly as it was rather than half a history.
            await uow.SaveChangesAsync(ct);

            // Every period touched is invalidated, not just the newest. A history spans years, and the reports and
            // arrears views for each of those months were computed without these payments in them.
            foreach (var period in created.Select(r => (r.BillingYear, r.BillingMonth)).Distinct())
            {
                await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                    tenantContext.TenantCode, request.FacilityCode, period.BillingYear, period.BillingMonth, ct);
            }
        }

        return Result<BulkImportPaymentResultDto>.Success(new BulkImportPaymentResultDto(
            TotalRows: request.Rows.Count,
            RecordedCount: results.Count(r => r.Outcome == ImportPaymentOutcome.RecordedPaid),
            PartialCount: results.Count(r => r.Outcome == ImportPaymentOutcome.RecordedPartial),
            AlreadyRecordedCount: results.Count(r => r.Outcome == ImportPaymentOutcome.AlreadyRecorded),
            RejectedCount: results.Count(r => r.Outcome == ImportPaymentOutcome.Rejected),
            TotalRecorded: created.Sum(r => r.Status == PaymentStatus.Paid ? r.BaseRentalAmount : r.PartialAmount),
            Results: results));
    }

    /// <summary>
    /// Says on the record that it came from the office's books rather than from a collection taken in the system,
    /// and keeps the date their sheet stated. Without this a historical payment is indistinguishable from one
    /// collected today, which an audit would rightly object to.
    /// </summary>
    private static string BuildRemark(ImportPaymentRow row)
    {
        var paidOn = row.DatePaid is { } d ? d.ToString("d MMM yyyy") : "date not stated";
        var occupant = string.IsNullOrWhiteSpace(row.Occupant) ? "occupant not stated" : row.Occupant!.Trim();
        return $"Imported from office records ({paidOn}; {occupant}).";
    }
}
