using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;

public record UpdateSlaughterCommand(
    string OwnerName,
    DateOnly TransactionDate,
    string ORNumber,
    List<AnimalEntry> Animals
) : IRequest<Result<bool>>;

public record AnimalEntry(
    AnimalType AnimalType,
    string? CustomAnimalType,
    int NumberOfHeads,
    decimal? CustomRate
);
