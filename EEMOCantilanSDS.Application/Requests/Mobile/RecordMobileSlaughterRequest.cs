using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record RecordMobileSlaughterRequest(
    string OwnerName,
    string ORNumber,
    AnimalType AnimalType,
    int NumberOfHeads,
    string? CustomAnimalType = null,
    decimal? CustomRate = null);
