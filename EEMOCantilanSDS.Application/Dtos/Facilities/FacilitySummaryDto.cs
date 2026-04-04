namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record FacilitySummaryDto(
    decimal TotalCollected,
    decimal TotalPending,
    decimal CollectionRate,
    int TotalStalls
);
