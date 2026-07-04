using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Dtos.Utilities;

/// <summary>A single meter-based NPM utility bill. Electricity and water are settled independently.</summary>
public record UtilityBillDto(
    Guid Id,
    Guid StallId,
    int BillingYear,
    int BillingMonth,
    decimal ElecPreviousReading,
    decimal ElecCurrentReading,
    decimal ElecRatePerKwh,
    decimal ElecConsumption,
    decimal ElecCharge,
    string ElecStatus,
    decimal ElecPartialAmount,
    decimal ElecBalanceDue,
    decimal WaterPreviousReading,
    decimal WaterCurrentReading,
    decimal WaterRatePerCubicMeter,
    decimal WaterConsumption,
    decimal WaterCharge,
    string WaterStatus,
    decimal WaterPartialAmount,
    decimal WaterBalanceDue,
    decimal TotalCharge,
    string Status,
    decimal AmountPaid,
    decimal BalanceDue,
    string? ElecORNumber,
    string? WaterORNumber,
    string? Remarks)
{
    public static UtilityBillDto From(UtilityBill b) => new(
        b.Id, b.StallId, b.BillingYear, b.BillingMonth,
        b.ElecPreviousReading, b.ElecCurrentReading, b.ElecRatePerKwh, b.ElecConsumption, b.ElecCharge,
        b.ElecStatus.ToString(), b.ElecPartialAmount, b.ElecBalanceDue,
        b.WaterPreviousReading, b.WaterCurrentReading, b.WaterRatePerCubicMeter, b.WaterConsumption, b.WaterCharge,
        b.WaterStatus.ToString(), b.WaterPartialAmount, b.WaterBalanceDue,
        b.TotalCharge, b.Status.ToString(), b.AmountPaid, b.BalanceDue,
        b.ElecORNumber, b.WaterORNumber, b.Remarks);
}
