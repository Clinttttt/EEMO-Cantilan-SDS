using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;

public class RecordSlaughterCommandHandler(
    ISlaughterRepository slaughterRepository,
    IFacilityRepository facilityRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<RecordSlaughterCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordSlaughterCommand request, CancellationToken ct)
    {
        var facility = await facilityRepository.GetByCodeAsync(FacilityCode.SLH, ct);
        if (facility is null)
            return Result<bool>.NotFound();

        SlaughterTransaction transaction = request.AnimalType switch
        {
            AnimalType.Hog => SlaughterTransaction.CreateHog(
                facility.Id,
                Guid.Empty,
                request.OwnerName,
                request.NumberOfHeads,
                request.ORNumber,
                request.TransactionDate,
                "Admin"),

            AnimalType.Carabao or AnimalType.Cow => SlaughterTransaction.CreateLargeAnimal(
                facility.Id,
                Guid.Empty,
                request.OwnerName,
                request.AnimalType,
                request.NumberOfHeads,
                request.ORNumber,
                request.TransactionDate,
                "Admin"),

            AnimalType.Other => SlaughterTransaction.CreateCustomAnimal(
                facility.Id,
                Guid.Empty,
                request.OwnerName,
                request.CustomAnimalType!,
                request.NumberOfHeads,
                request.CustomRate!.Value,
                request.ORNumber,
                request.TransactionDate,
                "Admin"),

            _ => throw new InvalidOperationException("Invalid animal type")
        };

        await slaughterRepository.AddAsync(transaction, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
