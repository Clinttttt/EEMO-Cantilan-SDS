using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;

/// <summary>Admin records/updates an NPM stall's meter readings and per-bill rates for a billing month.</summary>
public record RecordUtilityReadingCommand(
    Guid StallId,
    int BillingYear,
    int BillingMonth,
    decimal ElecPreviousReading,
    decimal ElecCurrentReading,
    decimal ElecRatePerKwh,
    decimal WaterPreviousReading,
    decimal WaterCurrentReading,
    decimal WaterRatePerCubicMeter,
    string? Remarks) : IRequest<Result<UtilityBillDto>>;
