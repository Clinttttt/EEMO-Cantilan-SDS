namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record CollectionPerformanceDto(
    int FullyPaidCount,
    int PartiallyPaidCount,
    int UnpaidCount
);
