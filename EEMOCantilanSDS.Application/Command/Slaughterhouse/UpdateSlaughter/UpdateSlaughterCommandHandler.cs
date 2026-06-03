using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;

public class UpdateSlaughterCommandHandler(
    ISlaughterRepository slaughterRepository,
    IFacilityRepository facilityRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateSlaughterCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateSlaughterCommand request, CancellationToken ct)
    {
        var facility = await facilityRepository.GetByCodeAsync(FacilityCode.SLH, ct);
        if (facility is null)
            return Result<bool>.NotFound();

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";

        var existingTransactions = await slaughterRepository.GetTransactionsByOwnerDateORAsync(
            request.OwnerName,
            request.TransactionDate,
            request.ORNumber,
            ct);

        foreach (var transaction in existingTransactions)
        {
            await slaughterRepository.RemoveAsync(transaction, ct);
        }

        foreach (var animal in request.Animals)
        {
            if (animal.NumberOfHeads <= 0) continue;

            SlaughterTransaction transaction = animal.AnimalType switch
            {
                AnimalType.Hog => SlaughterTransaction.CreateHog(
                    facility.Id,
                    collectorId,
                    request.OwnerName,
                    animal.NumberOfHeads,
                    request.ORNumber,
                    request.TransactionDate,
                    recordedBy),

                AnimalType.Carabao or AnimalType.Cow => SlaughterTransaction.CreateLargeAnimal(
                    facility.Id,
                    collectorId,
                    request.OwnerName,
                    animal.AnimalType,
                    animal.NumberOfHeads,
                    request.ORNumber,
                    request.TransactionDate,
                    recordedBy),

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
        return Result<bool>.Success(true);
    }
}
