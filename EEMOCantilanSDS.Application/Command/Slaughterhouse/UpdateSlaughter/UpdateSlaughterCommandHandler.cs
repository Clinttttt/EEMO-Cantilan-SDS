using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
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
        var existingTransactions = await slaughterRepository.GetTransactionsByOwnerDateORAsync(
            request.OwnerName,
            request.TransactionDate,
            request.ORNumber,
            ct);
        var approvedCustom = await slaughterRepository.GetApprovedCustomAnimalsAsync(ct) ?? [];
        var entries = new List<(AnimalEntry Animal, decimal? RatePerHead)>();
        foreach (var animal in request.Animals.Where(a => a.NumberOfHeads > 0))
        {
            var key = SlaughterRateKeys.For(animal.AnimalType);
            decimal? rate;
            var animalEntry = animal;
            if (key is { } rateKey)
                rate = rateSnapshot.ResolveOrNull(rateKey, request.TransactionDate);
            else
            {
                // Collectors and staff never invent a rate (IA-050). A custom animal is an approved one at its approved
                // rate, except a line already on this receipt that is saved back unchanged: historical CustomRate
                // evidence is preserved, never re-priced and never rewritten.
                var name = animal.CustomAnimalType?.Trim();
                var unchangedHistorical = existingTransactions.Any(t => t.AnimalType == AnimalType.Other
                    && string.Equals(t.CustomAnimalType, name, StringComparison.OrdinalIgnoreCase)
                    && t.RatePerHead == animal.CustomRate);
                var approved = approvedCustom.FirstOrDefault(a => a.IsActive
                    && string.Equals(a.AnimalName, name, StringComparison.OrdinalIgnoreCase));
                if (unchangedHistorical)
                    rate = animal.CustomRate;
                else if (approved is null)
                    return Result<bool>.Failure(
                        $"'{animal.CustomAnimalType}' is not an approved slaughter animal. The Head or an Admin must approve it, with its rate, before it can be recorded.",
                        ResultStatus.Invalid);
                else if (animal.CustomRate is { } typed && typed != approved.RatePerHead)
                    return Result<bool>.Failure(
                        $"The approved rate for {approved.AnimalName} is ₱{approved.RatePerHead:N2} per head. A different rate cannot be entered here.",
                        ResultStatus.Invalid);
                else
                {
                    rate = approved.RatePerHead;
                    animalEntry = animal with { CustomAnimalType = approved.AnimalName, CustomRate = approved.RatePerHead };
                }
            }

            if (key is { } required && rate is null)
                return Result<bool>.Failure(FeeRateMessages.NotStated(required));

            entries.Add((animalEntry, rate));
        }


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
