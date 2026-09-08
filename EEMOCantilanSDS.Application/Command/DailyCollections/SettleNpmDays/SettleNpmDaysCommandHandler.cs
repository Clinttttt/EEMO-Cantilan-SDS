using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;

public class SettleNpmDaysCommandHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    IPaymentRepository paymentRepository,
    IStallRepository stallRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    INpmMarketClosureRepository marketClosureRepository,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    IFeeRateResolver feeRateResolver,
    INpmMonthSettlementService monthSettlement,
    ITenantContext tenantContext, IClock clock) : IRequestHandler<SettleNpmDaysCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SettleNpmDaysCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        // Daily settlement is NPM-only; every other facility is monthly and uses RecordPayment.
        if (stall.Facility?.Code != FacilityCode.NPM)
            return Result<bool>.Failure("Only New Public Market (daily) accounts are settled by day here.", ResultStatus.Invalid);

        // Collectors may only act on an assigned facility (same rule as recording a single daily collection). One rule for both
        // settle paths - see NpmSettlementAccess for why it is not written out here.
        if (!await NpmSettlementAccess.MaySettleMarketCollectionsAsync(currentUser, collectorRepository, ct))
            return Result<bool>.Forbidden();

        var dates = request.Dates.Distinct().OrderBy(d => d).ToList();
        if (dates.Count == 0)
            return Result<bool>.Failure("Select at least one day.", ResultStatus.Invalid);

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";
        var orNumber = request.ORNumber?.Trim();
        var today = clock.PhilippineToday;
        // The occupancy windows, computed once. A day is settleable when SOME term answers for it — not only the
        // stall's current one, or arrears left behind by a lessee who has gone could never be collected.
        var occupancies = stall.Occupancies(today);
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        // The fee this stall is settled at, or nothing to settle with. A fee the office has never stated cannot be
        // raised against a vendor, and must not be quietly taken as zero either: the amounts below are what the office
        // will reconcile against by hand. Asked of the STALL, so an office that prices its market's areas apart is
        // answered for the area this stall stands in; where it states one rate for the whole market, that is what
        // answers, exactly as before.
        if (NpmDailyFee.ForStallOrNull(stall, snapshot, clock.PhilippineToday) is null)
            return Result<bool>.Failure(FeeRateMessages.NotStated(FeeRateKey.NpmDailyStall));

        // Load existing collections + facility closures for every month the selected dates span.
        var months = dates.Select(d => (d.Year, d.Month)).Distinct().ToList();
        var existing = new Dictionary<DateOnly, DailyCollection>();
        var closedDates = new HashSet<DateOnly>();
        foreach (var (year, month) in months)
        {
            foreach (var dc in await dailyCollectionRepository.GetByStallAndMonthAsync(request.StallId, year, month, ct))
                existing[dc.CollectionDate] = dc;
            foreach (var c in await marketClosureRepository.GetByMonthAsync(year, month, ct))
                closedDates.Add(c.ClosureDate);
        }

        // WHAT EACH MONTH CAN ACTUALLY BE CHARGED, which is not the sum of its days' fees.
        //
        // Where the office lets a stall for a monthly rent, the month owes that rent whatever its calendar gave it: a 31-day month
        // at ₱30 owes ₱900, not ₱930. This handler charged every selected day its own fee with no ceiling, so settling a whole
        // 31-day month took ₱930 - thirty pesos more than the month owed - while the screen that offered it quoted ₱900, because the
        // arrears row prices the same days as Math.Min(listed, month payable). Found by audit 2026-09-08.
        //
        // The ceiling is taken from the SAME settlement the quote uses, so the figure a collector is shown and the figure the ledger
        // records are one number by construction rather than by two calculations agreeing.
        var monthCap = new Dictionary<(int Year, int Month), decimal>();
        foreach (var key in months)
            monthCap[key] = (await monthSettlement.ComputePayableAsync(stall, key.Year, key.Month, ct)).Amount;

        var chargedByMonth = new Dictionary<(int Year, int Month), decimal>();

        var settled = new List<DailyCollection>();
        foreach (var day in dates)
        {
            if (day > today) continue;                                  // never settle future days
            if (!occupancies.Any(o => o.Start <= day && day <= o.BillableEnd))
                continue;                                              // nobody owes anything for that day
            if (closedDates.Contains(day))
                continue;                                               // facility-wide closure — nothing owed

            existing.TryGetValue(day, out var dc);
            if (dc is not null && (dc.IsPaid || dc.IsAbsent))
                continue;                                               // already collected or excused

            var fee = NpmDailyFee.ForStall(stall, snapshot, day);

            // Never past what the month itself owes. A day beyond the ceiling is left alone rather than settled at a reduced fee:
            // the month's rent is met, so nothing is owed for it, and recording a part-fee against a day would misstate what the
            // office received on that day.
            var monthKey = (day.Year, day.Month);
            var chargedSoFar = chargedByMonth.GetValueOrDefault(monthKey);
            if (monthCap.TryGetValue(monthKey, out var cap) && chargedSoFar + fee > cap)
                continue;

            chargedByMonth[monthKey] = chargedSoFar + fee;

            if (dc is null)
            {
                dc = DailyCollection.Create(request.StallId, day, recordedBy, fee);
                dc.MarkPaid(orNumber: string.Empty, collectorId: collectorId, fishKilos: null, updatedBy: recordedBy);
                await dailyCollectionRepository.AddAsync(dc, ct);
            }
            else
            {
                dc.MarkPaid(orNumber: string.Empty, collectorId: collectorId, fishKilos: null, updatedBy: recordedBy);
            }
            settled.Add(dc);
        }

        if (settled.Count == 0)
            return Result<bool>.Failure("None of the selected days could be settled (already paid, excused, market closed, or no term owes anything for them).", ResultStatus.Invalid);

        // One physical receipt (OR) covers all the selected days — stall-aware uniqueness (same rule as
        // the slaughterhouse's one-receipt-per-visit), so the same OR may repeat across this stall's days.
        if (!string.IsNullOrWhiteSpace(orNumber))
        {
            if (!await paymentRepository.IsDailyCollectionOrAvailableForStallAsync(orNumber, request.StallId, ct))
                return Result<bool>.Failure("OR number already exists.", ResultStatus.Conflict);
            foreach (var dc in settled)
                dc.SetOrNumber(orNumber, recordedBy);
        }

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var (year, month) in months)
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(tenantContext.TenantCode, FacilityCode.NPM, year, month, ct);

        return Result<bool>.Success(true);
    }
}
