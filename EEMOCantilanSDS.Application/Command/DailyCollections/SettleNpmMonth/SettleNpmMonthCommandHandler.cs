using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
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
    ITenantContext tenantContext) : IRequestHandler<SettleNpmMonthCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SettleNpmMonthCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        // Daily settlement is NPM-only; every other facility is monthly and uses RecordPayment.
        if (stall.Facility?.Code != FacilityCode.NPM)
            return Result<bool>.Failure("Only New Public Market (daily) accounts are settled by month here.", 400);

        // Collectors may only act on an assigned facility (same rule as recording a single daily collection).
        var isCollectorRequest = currentUser.Role == "Collector";
        if (isCollectorRequest)
        {
            if (currentUser.CollectorId is not { } actingCollectorId || stall.Facility is null)
                return Result<bool>.Forbidden();
            var collector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (collector is null || !collector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM))
                return Result<bool>.Forbidden();
        }

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";
        var orNumber = request.ORNumber?.Trim();

        var monthStart = new DateOnly(request.Year, request.Month, 1);
        var monthEnd = new DateOnly(request.Year, request.Month, DateTime.DaysInMonth(request.Year, request.Month));
        var today = PhilippineTime.Today;
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
                "No occupancy of this stall answers for that month — open that period's own account to settle it.", 400);

        var existing = (await dailyCollectionRepository.GetByStallAndMonthAsync(request.StallId, request.Year, request.Month, ct))
            .ToDictionary(dc => dc.CollectionDate);
        var closedDates = (await marketClosureRepository.GetByMonthAsync(request.Year, request.Month, ct))
            .Select(c => c.ClosureDate)
            .ToHashSet();

        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);

        // What this month owes and what is already in, so one act of "settle the month" collects the month's rent
        // and no more: a 31-day month settles ₱900, not ₱930. Absent days and closures owe nothing, so they never
        // count. A further day the payor actually traded is still collectable from the daily calendar — that is the
        // day-to-day path, and its fee is revenue beyond the rent rather than an arrear.
        var chargeableDays = 0;
        var alreadyCollected = 0m;
        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day > today) break;
            if (day < occupancy.Start || day > occupancy.BillableEnd) continue;
            if (closedDates.Contains(day)) continue;

            existing.TryGetValue(day, out var known);
            if (known is not null && known.IsAbsent) continue;

            chargeableDays++;
            if (known is not null && known.IsPaid) alreadyCollected += known.DailyFee;
        }

        var monthCeilingDay = today < monthEnd ? today : monthEnd;
        var monthCeiling = DomainRules.DailyBilledMonthCharge(
            stall.ResolveDailyFee(snapshot.Resolve(FeeRateKey.NpmDailyStall, monthCeilingDay)), chargeableDays);
        var collectable = monthCeiling - alreadyCollected;

        var settled = new List<DailyCollection>();
        var accumulated = 0m;
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

            var fee = stall.ResolveDailyFee(snapshot.Resolve(FeeRateKey.NpmDailyStall, day));
            if (accumulated + fee > collectable)
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

        // Stamp the receipt (OR) on EVERY settled day — one physical receipt covers the whole month
        // (allowed for a single stall since the daily-collection OR check is stall-aware, mirroring the
        // slaughterhouse's one-receipt-per-visit rule).
        if (settled.Count > 0 && !string.IsNullOrWhiteSpace(orNumber))
        {
            if (!await paymentRepository.IsDailyCollectionOrAvailableForStallAsync(orNumber, request.StallId, ct))
                return Result<bool>.Failure("OR number already exists.", 409);
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
}
