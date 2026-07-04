using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Dtos.Utilities;

/// <summary>One month in a stall's full utility history — every electricity &amp; water detail in one row.</summary>
public record UtilityHistoryRowDto(
    int Year,
    int Month,
    decimal ElecPreviousReading,
    decimal ElecCurrentReading,
    decimal ElecConsumption,
    decimal ElecRatePerKwh,
    decimal ElecCharge,
    decimal WaterPreviousReading,
    decimal WaterCurrentReading,
    decimal WaterConsumption,
    decimal WaterRatePerCubicMeter,
    decimal WaterCharge,
    decimal TotalCharge,
    string Status,
    string ElecStatus,
    string WaterStatus,
    decimal AmountPaid,
    decimal BalanceDue,
    string? ElecORNumber,
    string? WaterORNumber,
    DateTime? ElecPaidAt,
    DateTime? WaterPaidAt)
{
    public static UtilityHistoryRowDto From(UtilityBill b) => new(
        b.BillingYear, b.BillingMonth,
        b.ElecPreviousReading, b.ElecCurrentReading, b.ElecConsumption, b.ElecRatePerKwh, b.ElecCharge,
        b.WaterPreviousReading, b.WaterCurrentReading, b.WaterConsumption, b.WaterRatePerCubicMeter, b.WaterCharge,
        b.TotalCharge, b.Status.ToString(), b.ElecStatus.ToString(), b.WaterStatus.ToString(),
        b.AmountPaid, b.BalanceDue, b.ElecORNumber, b.WaterORNumber, b.ElecPaidAt, b.WaterPaidAt);
}
