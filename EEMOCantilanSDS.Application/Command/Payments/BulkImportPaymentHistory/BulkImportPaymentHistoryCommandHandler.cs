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
    ITenantContext tenantContext) : IRequestHandler<BulkImportPaymentHistoryCommand, Result<BulkImportPaymentResultDto>>
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
            if (row.AmountPaid <= 0m) { Reject("The amount paid must be more than zero."); continue; }
            if (row.AmountPaid > MaxAmount) { Reject("The amount paid is implausibly large."); continue; }
            if (string.IsNullOrWhiteSpace(row.OrNumber))
            {
                // Every real payment carries a receipt, and the arrears lists treat one without as needing
                // follow-up - so importing history without it would raise a false alarm on every row.
                Reject("An OR number is required: a recorded payment without one is reported as missing a receipt.");
                continue;
            }

            if (!stallsByNo.TryGetValue(stallNo, out var stall))
            {
                Reject($"No stall {stallNo} in this facility{(request.Section.HasValue ? " and section" : "")}. " +
                       "Import the stallholders first.");
                continue;
            }

            // The occupancy in force for the month being PAID FOR, not the one in force today. A stall outlives its
            // lessees, so a payment for March 2024 belongs to whoever held it then - attaching it to the current
            // contract would credit this lessee with the previous one's money.
            var contract = stall.Contracts.FirstOrDefault(c => c.BillsCalendarMonth(row.BillingYear, row.BillingMonth));
            if (contract is null)
            {
                Reject($"No contract on stall {stallNo} bills {period}. The month is before the term started, " +
                       "after it ended, or the term on record is wrong.");
                continue;
            }

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
