using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record UpdateMobileSlaughterRequest(
    string OwnerName,
    DateOnly TransactionDate,
    string ORNumber,
    List<MobileAnimalEntry> Animals);

public sealed record MobileAnimalEntry(
    AnimalType AnimalType,
    int NumberOfHeads,
    string? CustomAnimalType = null,
    decimal? CustomRate = null);
