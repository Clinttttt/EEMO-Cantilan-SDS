using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
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
    ITenantContext tenantContext) : IRequestHandler<SettleNpmDaysCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SettleNpmDaysCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        // Daily settlement is NPM-only; every other facility is monthly and uses RecordPayment.
        if (stall.Facility?.Code != FacilityCode.NPM)
            return Result<bool>.Failure("Only New Public Market (daily) accounts are settled by day here.", 400);

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

        var dates = request.Dates.Distinct().OrderBy(d => d).ToList();
        if (dates.Count == 0)
            return Result<bool>.Failure("Select at least one day.", 400);

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";
        var orNumber = request.ORNumber?.Trim();
        var today = PhilippineTime.Today;
        var contract = stall.Contracts.FirstOrDefault(c => c.IsActive);
        var snapshot = await feeRateResolver.GetSnapshotAsync(ct);

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

        var settled = new List<DailyCollection>();
        foreach (var day in dates)
        {
            if (day > today) continue;                                  // never settle future days
            if (contract is null || !(contract.EffectivityDate <= day && day <= contract.ExpiryDate))
                continue;                                               // not under an effective contract
            if (closedDates.Contains(day))
                continue;                                               // facility-wide closure — nothing owed

            existing.TryGetValue(day, out var dc);
            if (dc is not null && (dc.IsPaid || dc.IsAbsent))
                continue;                                               // already collected or excused

            var fee = stall.ResolveDailyFee(snapshot.Resolve(FeeRateKey.NpmDailyStall, day));
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
            return Result<bool>.Failure("None of the selected days could be settled (already paid, closed, or outside the contract).", 400);

        // One physical receipt (OR) covers all the selected days — stall-aware uniqueness (same rule as
        // the slaughterhouse's one-receipt-per-visit), so the same OR may repeat across this stall's days.
        if (!string.IsNullOrWhiteSpace(orNumber))
        {
            if (!await paymentRepository.IsDailyCollectionOrAvailableForStallAsync(orNumber, request.StallId, ct))
                return Result<bool>.Failure("OR number already exists.", 409);
            foreach (var dc in settled)
                dc.SetOrNumber(orNumber, recordedBy);
        }

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var (year, month) in months)
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(tenantContext.TenantCode, FacilityCode.NPM, year, month, ct);

        return Result<bool>.Success(true);
    }
}
