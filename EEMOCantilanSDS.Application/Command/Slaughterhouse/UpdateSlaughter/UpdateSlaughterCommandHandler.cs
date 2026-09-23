using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;

public class UpdateSlaughterCommandHandler(
    ISlaughterRepository slaughterRepository,
    IFacilityRepository facilityRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    IFeeRateResolver feeRateResolver,
    ITenantContext tenantContext) : IRequestHandler<UpdateSlaughterCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateSlaughterCommand request, CancellationToken ct)
    {
        var facility = await facilityRepository.GetByCodeAsync(FacilityCode.SLH, ct);
        if (facility is null)
            return Result<bool>.NotFound();

        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingId)
                return Result<bool>.Forbidden();
            var actor = await collectorRepository.GetByIdAsync(actingId, ct);
            if (actor is null || !actor.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.SLH))
                return Result<bool>.Forbidden();
        }

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";

        // The edit form retains the original activity date. Resolve canonical per-head rates as of that date,
        // just as the create path does, before replacing any rows. Custom animals retain their submitted
        // registry rate and do not use fixed FeeRateKeys.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var entries = new List<(AnimalEntry Animal, decimal? RatePerHead)>();
        foreach (var animal in request.Animals.Where(a => a.NumberOfHeads > 0))
        {
            var key = SlaughterRateKeys.For(animal.AnimalType);
            var rate = key is { } rateKey
                ? rateSnapshot.ResolveOrNull(rateKey, request.TransactionDate)
                : animal.CustomRate;

            if (key is { } required && rate is null)
                return Result<bool>.Failure(FeeRateMessages.NotStated(required));

            entries.Add((animal, rate));
        }

        var existingTransactions = await slaughterRepository.GetTransactionsByOwnerDateORAsync(
            request.OwnerName,
            request.TransactionDate,
            request.ORNumber,
            ct);

        foreach (var transaction in existingTransactions)
        {
            await slaughterRepository.RemoveAsync(transaction, ct);
        }

        foreach (var (animal, ratePerHead) in entries)
        {
            SlaughterTransaction transaction = animal.AnimalType switch
            {
                AnimalType.Hog => SlaughterTransaction.CreateHog(
                    facility.Id,
                    collectorId,
                    request.OwnerName,
                    animal.NumberOfHeads,
                    request.ORNumber,
                    request.TransactionDate,
                    recordedBy,
                    ratePerHead: ratePerHead),

                AnimalType.Carabao or AnimalType.Cow => SlaughterTransaction.CreateLargeAnimal(
                    facility.Id,
                    collectorId,
                    request.OwnerName,
                    animal.AnimalType,
                    animal.NumberOfHeads,
                    request.ORNumber,
                    request.TransactionDate,
                    recordedBy,
                    ratePerHead: ratePerHead),

                AnimalType.Other => SlaughterTransaction.CreateCustomAnimal(
                    facility.Id,
                    collectorId,
                    request.OwnerName,
                    animal.CustomAnimalType!,
                    animal.NumberOfHeads,
                    animal.CustomRate!.Value,
                    request.ORNumber,
                    request.TransactionDate,
                    recordedBy),

                _ => throw new InvalidOperationException("Invalid animal type")
            };

            await slaughterRepository.AddAsync(transaction, ct);
        }

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
