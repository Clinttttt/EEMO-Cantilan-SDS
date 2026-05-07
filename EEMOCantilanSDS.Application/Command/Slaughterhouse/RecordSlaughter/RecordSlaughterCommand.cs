using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;

public record RecordSlaughterCommand(
    string OwnerName,
    DateOnly TransactionDate,
    string ORNumber,
    AnimalType AnimalType,
    string? CustomAnimalType,
    int NumberOfHeads,
    decimal? CustomRate
) : IRequest<Result<bool>>;
