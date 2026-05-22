namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record PaymentStatusDistributionDto(
    int PaidCount,
    decimal PaidPercentage,
    int PartialCount,
    decimal PartialPercentage,
    int UnpaidCount,
    decimal UnpaidPercentage
);
