using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;

public class SettleNpmMonthCommandHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    IPaymentRepository paymentRepository,
    IStallRepository stallRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    INpmMarketClosureRepository marketClosureRepository,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    IFeeRateResolver feeRateResolver,
    ITenantContext tenantContext, IClock clock) : IRequestHandler<SettleNpmMonthCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SettleNpmMonthCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        // Daily settlement is NPM-only; every other facility is monthly and uses RecordPayment.
        if (stall.Facility?.Code != FacilityCode.NPM)
            return Result<bool>.Failure("Only New Public Market (daily) accounts are settled by month here.", ResultStatus.Invalid);

        // Collectors may only act on an assigned facility (same rule as recording a single daily collection). One rule for both
        // settle paths - see NpmSettlementAccess for why it is not written out here.
        var isCollectorRequest = currentUser.Role == "Collector";
        if (!await NpmSettlementAccess.MaySettleMarketCollectionsAsync(currentUser, collectorRepository, ct))
            return Result<bool>.Forbidden();

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";
        var orNumber = request.ORNumber?.Trim();

        var monthStart = new DateOnly(request.Year, request.Month, 1);
        var monthEnd = new DateOnly(request.Year, request.Month, DateTime.DaysInMonth(request.Year, request.Month));
        var today = clock.PhilippineToday;
        // Whose month this is. A row that names its term settles that term. When none is named, the term that
        // answers for THIS MONTH is what "the month" means — on a stall since re-let the most recent occupancy is
        // a different account, so settling a past month against it collected nothing while reporting success.
        // On a handover month the later occupancy is taken, which is what the current occupant's row means; the
        // earlier lessee's days are settled from their own row, which names their term.
        var occupancy = request.ContractId is { } namedTerm && namedTerm != Guid.Empty
            ? stall.ResolveOccupancy(namedTerm, today)
            : stall.OccupanciesOverlapping(monthStart, monthEnd, today).LastOrDefault()
                ?? stall.ResolveOccupancy(null, today);

        // A month no occupancy of this stall answers for can never be settled here. Reporting that as success is
        // what left the office believing money had been recorded when nothing had.
        if (occupancy is null || occupancy.BillableEnd < monthStart || monthEnd < occupancy.Start)
            return Result<bool>.Failure(
                "No occupancy of this stall answers for that month — open that period's own account to settle it.", ResultStatus.Invalid);

        var existing = (await dailyCollectionRepository.GetByStallAndMonthAsync(request.StallId, request.Year, request.Month, ct))
            .ToDictionary(dc => dc.CollectionDate);
        var closedDates = (await marketClosureRepository.GetByMonthAsync(request.Year, request.Month, ct))
            .Select(c => c.ClosureDate)
            .ToHashSet();

        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);
        // The fee this stall is settled at, or nothing to settle with. A fee the office has never stated cannot be
        // raised against a vendor, and must not be quietly taken as zero either: the amounts below are what the office
        // will reconcile against by hand. Asked of the STALL, so an office that prices its market's areas apart is
        // answered for the area this stall stands in.
        if (NpmDailyFee.ForStallOrNull(stall, snapshot, clock.PhilippineToday) is null)
            return Result<bool>.Failure(FeeRateMessages.NotStated(FeeRateKey.NpmDailyStall));

        // The month's own ledger: its contractual rent (₱900 for a month held in full, whatever the calendar gave
        // it), the days nothing is owed for, and what is already in. One act of "settle the month" collects what the
        // month owes and no more — a 31-day month settles ₱900, and a closed February settles ₱900 as twenty-eight
        // installments plus its ₱60 month-end adjustment. A further day the payor traded is still collectable from
        // the daily calendar, where its fee is revenue beyond the rent rather than an arrear.
        var daysHeld = 0;
        var daysForgiven = 0;
        var alreadyCollected = 0m;
        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day > today) break;
            if (day < occupancy.Start || day > occupancy.BillableEnd) continue;

            daysHeld++;

            if (closedDates.Contains(day)) { daysForgiven++; continue; }

            existing.TryGetValue(day, out var known);
            if (known is not null && known.IsAbsent) { daysForgiven++; continue; }
            if (known is not null && known.IsPaid) alreadyCollected += known.DailyFee;
        }

        var monthCeilingDay = today < monthEnd ? today : monthEnd;
        var monthFee = NpmDailyFee.ForStall(stall, snapshot, monthCeilingDay);
        // The month's rent. Where the office states a monthly rent it wins, as it always has; where it states none the
        // month is thirty of THIS stall's daily fee, which is the area's fee for an office that prices its areas apart.
        var monthRent = stall.ResolveMonthlyRent(
            NpmDailyFee.ForStall(stall, snapshot, monthCeilingDay),
            snapshot.Resolve(FeeRateKey.NpmMonthlyStall, monthCeilingDay));
        var obligation = DomainRules.DailyBilledMonthObligation(monthFee, monthRent, monthEnd.Day, daysHeld);
        var credit = DomainRules.DailyBilledMonthCredit(monthFee, obligation, daysHeld, daysForgiven);
        var collectable = DomainRules.DailyBilledMonthOutstanding(obligation, alreadyCollected, credit);

        var settled = new List<DailyCollection>();
        var accumulated = 0m;
        // The installments this settlement may collect: the month's outstanding balance less any month-end
        // adjustment, which rides on the last installment once the month has closed.
        var monthClosed = today > monthEnd;
        var installmentsOwed = 0m;
        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day > today) break;
            if (day < occupancy.Start || day > occupancy.BillableEnd) continue;
            if (closedDates.Contains(day)) continue;
            existing.TryGetValue(day, out var known);
            if (known is not null && (known.IsPaid || known.IsAbsent)) continue;
            installmentsOwed += NpmDailyFee.ForStall(stall, snapshot, day);
        }
        var adjustment = monthClosed && collectable > installmentsOwed ? collectable - installmentsOwed : 0m;
        var installmentCap = collectable - adjustment;

        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day > today) break;                                     // never settle future days
            if (day < occupancy.Start || day > occupancy.BillableEnd)
                continue;                                               // not this lessee's day to answer for
            if (closedDates.Contains(day))
                continue;                                               // facility-wide closure — nothing owed

            existing.TryGetValue(day, out var dc);
            if (dc is not null && (dc.IsPaid || dc.IsAbsent))
                continue;                                               // already collected or excused

            var fee = NpmDailyFee.ForStall(stall, snapshot, day);
            if (accumulated + fee > installmentCap)
                break;                                                  // the month's rent is settled
            accumulated += fee;

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

        // A closed month short of its rent settles the difference with its last installment, so its obligation is
        // met in full and nothing is left behind to read as arrears. When every day was already collected at the
        // stall there is no new installment to carry it, so it lands on the last one taken — otherwise a payor who
        // paid every day of a short month would owe that difference for ever.
        if (adjustment > 0m)
        {
            var carrier = settled.Count > 0 ? settled[^1] : LastCollectedOf(existing, monthStart, monthEnd, occupancy);
            if (carrier is not null)
            {
                carrier.AddMonthEndAdjustment(adjustment, recordedBy);
                if (settled.Count == 0) settled.Add(carrier);
            }
        }

        // Stamp the receipt (OR) on EVERY settled day — one physical receipt covers the whole month
        // (allowed for a single stall since the daily-collection OR check is stall-aware, mirroring the
        // slaughterhouse's one-receipt-per-visit rule).
        if (settled.Count > 0 && !string.IsNullOrWhiteSpace(orNumber))
        {
            if (!await paymentRepository.IsDailyCollectionOrAvailableForStallAsync(orNumber, request.StallId, ct))
                return Result<bool>.Failure("OR number already exists.", ResultStatus.Conflict);
            foreach (var dc in settled)
                dc.SetOrNumber(orNumber, recordedBy);
        }

        if (settled.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                tenantContext.TenantCode, FacilityCode.NPM, request.Year, request.Month, ct);
        }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// The last installment actually collected for this occupancy in the month — where a month-end adjustment lands
    /// when no uncollected day is left to carry it.
    /// </summary>
    private static DailyCollection? LastCollectedOf(
        IReadOnlyDictionary<DateOnly, DailyCollection> existing,
        DateOnly monthStart,
        DateOnly monthEnd,
        StallOccupancy occupancy)
    {
        DailyCollection? last = null;
        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day < occupancy.Start || day > occupancy.BillableEnd) continue;
            if (existing.TryGetValue(day, out var dc) && dc.IsPaid) last = dc;
        }

        return last;
    }
}
