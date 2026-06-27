using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;

/// <summary>
/// Closes (freezes) or reopens a stall. A close stops the stall from being a current payor and from
/// accruing any obligation while closed; existing history/payments are untouched. A reopen RESUMES
/// billing and persists the frozen span [closedOn, today) as EXCUSED so the closure is never
/// back-billed as arrears — monthly facilities get an excused billing month per closed month; NPM
/// (daily) gets an absent (₱0) day per closed day. A real payment on a closed day is never overwritten.
/// </summary>
public class ToggleStallStatusCommandHandler(
    IStallRepository stallRepository,
    IStallMonthlyExceptionRepository monthlyExceptionRepository,
    IDailyCollectionRepository dailyCollectionRepository,
    IPaymentRepository paymentRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<ToggleStallStatusCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ToggleStallStatusCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall == null)
            return Result<bool>.NotFound();

        var actor = currentUser.Username ?? "Admin";

        if (request.Close)
        {
            stall.Close(PhilippineTime.Today, actor);
        }
        else
        {
            var closedOn = stall.ClosedAt;
            stall.Reopen(actor);

            if (closedOn is { } start)
                await ExcuseClosurePeriodAsync(stall, start, PhilippineTime.Today, actor, ct);
        }

        await stallRepository.UpdateAsync(stall, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Persists the frozen span [start, reopenOn) as excused for every contract-effective day/month.
    /// NPM → absent daily collections; monthly facilities → excused billing months (full-month).
    /// Idempotent: skips days/months already absent/excused and never overwrites a real payment.
    /// </summary>
    private async Task ExcuseClosurePeriodAsync(
        Stall stall, DateOnly start, DateOnly reopenOn, string actor, CancellationToken ct)
    {
        // Closed days are [start, reopenOn) — the reopen day itself is active again.
        var lastClosed = reopenOn.AddDays(-1);
        if (lastClosed < start) return;   // closed and reopened the same day → nothing to excuse

        bool ContractEffectiveOn(DateOnly d) =>
            stall.Contracts.Any(c => c.IsActive
                && c.EffectivityDate <= d && c.EffectivityDate.AddYears(c.DurationYears) >= d);

        if (stall.Facility?.Code == FacilityCode.NPM)
        {
            for (var d = start; d <= lastClosed; d = d.AddDays(1))
            {
                if (!ContractEffectiveOn(d)) continue;

                var existing = await dailyCollectionRepository.GetByStallAndDateAsync(stall.Id, d, ct);
                if (existing is null)
                {
                    var absent = DailyCollection.Create(stall.Id, d, actor);
                    absent.MarkAbsent(actor);
                    await dailyCollectionRepository.AddAsync(absent, ct);
                }
                else if (!existing.IsPaid && !existing.IsAbsent)
                {
                    existing.MarkAbsent(actor);   // tracked entity → persists on SaveChanges
                }
            }
        }
        else
        {
            var cursor = new DateOnly(start.Year, start.Month, 1);
            var lastMonth = new DateOnly(lastClosed.Year, lastClosed.Month, 1);
            while (cursor <= lastMonth)
            {
                var mStart = cursor;
                var mEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                var effective = stall.Contracts.Any(c => c.IsActive
                    && c.EffectivityDate <= mEnd && c.EffectivityDate.AddYears(c.DurationYears) >= mStart);

                if (effective && await monthlyExceptionRepository.GetAsync(stall.Id, cursor.Year, cursor.Month, ct) is null)
                {
                    // Never excuse a month the vendor already PAID in full — keep it "Paid", not "Excused".
                    // (Unpaid/partial months in the closed span are still excused so they aren't arrears.)
                    var existingRecord = await paymentRepository.GetPaymentRecordAsync(stall.Id, cursor.Year, cursor.Month, ct);
                    if (existingRecord is not { Status: PaymentStatus.Paid })
                    {
                        await monthlyExceptionRepository.AddAsync(
                            StallMonthlyException.Create(
                                stall.Id, cursor.Year, cursor.Month,
                                MonthlyExceptionReason.TemporaryClosure,
                                "Stall closure (frozen period)", actor),
                            ct);
                    }
                }
                cursor = cursor.AddMonths(1);
            }
        }
    }
}
