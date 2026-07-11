using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
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
        var contract = stall.Contracts.FirstOrDefault(c => c.IsActive);

        var existing = (await dailyCollectionRepository.GetByStallAndMonthAsync(request.StallId, request.Year, request.Month, ct))
            .ToDictionary(dc => dc.CollectionDate);
        var closedDates = (await marketClosureRepository.GetByMonthAsync(request.Year, request.Month, ct))
            .Select(c => c.ClosureDate)
            .ToHashSet();

        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);

        DailyCollection? lastSettled = null;
        var settledCount = 0;
        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (day > today) break;                                     // never settle future days
            if (contract is null || !(contract.EffectivityDate <= day && day <= contract.ExpiryDate))
                continue;                                               // not under an effective contract
            if (closedDates.Contains(day))
                continue;                                               // facility-wide closure — nothing owed

            existing.TryGetValue(day, out var dc);
            if (dc is not null && (dc.IsPaid || dc.IsAbsent))
                continue;                                               // already collected or excused

            var fee = snapshot.Resolve(FeeRateKey.NpmDailyStall, day);
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
            lastSettled = dc;
            settledCount++;
        }

        // Stamp the receipt (OR) on the month's last settled day — one OR per month, matching the
        // existing NPM Add-OR pattern (and avoiding the per-day OR unique-index conflict).
        if (lastSettled is not null && !string.IsNullOrWhiteSpace(orNumber))
        {
            if (!await paymentRepository.IsORNumberUniqueAsync(orNumber, ct))
                return Result<bool>.Failure("OR number already exists.", 409);
            lastSettled.MarkPaid(orNumber: orNumber, collectorId: collectorId, fishKilos: null, updatedBy: recordedBy);
        }

        if (settledCount > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                tenantContext.TenantCode, FacilityCode.NPM, request.Year, request.Month, ct);
        }

        return Result<bool>.Success(true);
    }
}
