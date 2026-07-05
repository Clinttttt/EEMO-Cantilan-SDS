using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;

public class RecordSlaughterCommandHandler(
    ISlaughterRepository slaughterRepository,
    IFacilityRepository facilityRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    IFeeRateResolver feeRateResolver,
    ITenantContext tenantContext) : IRequestHandler<RecordSlaughterCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordSlaughterCommand request, CancellationToken ct)
    {
        var facility = await facilityRepository.GetByCodeAsync(FacilityCode.SLH, ct);
        if (facility is null)
            return Result<bool>.NotFound();

        // Collectors may only record at the slaughterhouse if assigned to it; admins are unrestricted.
        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<bool>.Forbidden();

            var actingCollector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (actingCollector is null ||
                !actingCollector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.SLH))
            {
                return Result<bool>.Forbidden();
            }
        }

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";

        // Resolve this municipality's per-head rates as of the transaction date (constant fallback, so
        // Cantilan stamps the same ₱250 hog / ₱365 large totals). The audit breakdown stays as the ordinance
        // components; only the per-head total is data-driven in Phase 4B.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);

        SlaughterTransaction transaction = request.AnimalType switch
        {
            AnimalType.Hog => SlaughterTransaction.CreateHog(
                facility.Id,
                collectorId,
                request.OwnerName,
                request.NumberOfHeads,
                request.ORNumber,
                request.TransactionDate,
                recordedBy,
                ratePerHead: rateSnapshot.Resolve(FeeRateKey.SlhHogPerHead, request.TransactionDate)),

            AnimalType.Carabao or AnimalType.Cow => SlaughterTransaction.CreateLargeAnimal(
                facility.Id,
                collectorId,
                request.OwnerName,
                request.AnimalType,
                request.NumberOfHeads,
                request.ORNumber,
                request.TransactionDate,
                recordedBy,
                ratePerHead: rateSnapshot.Resolve(FeeRateKey.SlhLargePerHead, request.TransactionDate)),

            AnimalType.Other => SlaughterTransaction.CreateCustomAnimal(
                facility.Id,
                collectorId,
                request.OwnerName,
                request.CustomAnimalType!,
                request.NumberOfHeads,
                request.CustomRate!.Value,
                request.ORNumber,
                request.TransactionDate,
                recordedBy),

            _ => throw new InvalidOperationException("Invalid animal type")
        };

        if (request.ClientOperationId is { } clientOpId)
            transaction.SetClientOperationId(clientOpId);

        await slaughterRepository.AddAsync(transaction, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            FacilityCode.SLH,
            request.TransactionDate.Year,
            request.TransactionDate.Month,
            ct);

        return Result<bool>.Success(true);
    }
}
