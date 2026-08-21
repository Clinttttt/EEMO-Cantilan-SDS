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

        // This municipality's own per-head rates as of the transaction date. A rate this office has not stated
        // is not borrowed from anywhere: a slaughter fee it never set cannot be raised against an owner, so the
        // record is refused and the office is told which rate to set. The audit breakdown stays as the ordinance
        // components; only the per-head total is data-driven.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);

        var perHeadKey = request.AnimalType switch
        {
            AnimalType.Hog => FeeRateKey.SlhHogPerHead,
            AnimalType.Carabao or AnimalType.Cow => FeeRateKey.SlhLargePerHead,
            _ => (FeeRateKey?)null,      // a custom animal carries its own rate from the LGU's registry
        };

        if (perHeadKey is { } required && rateSnapshot.ResolveOrNull(required, request.TransactionDate) is null)
            return Result<bool>.Failure(FeeRateMessages.NotStated(required));

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
