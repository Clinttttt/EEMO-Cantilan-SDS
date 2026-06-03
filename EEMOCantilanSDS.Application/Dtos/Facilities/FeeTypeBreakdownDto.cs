namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record FeeTypeBreakdownDto(
    decimal DailyFeeAmount,
    decimal FishFeeAmount,
    string? FishKiloComparison
);
