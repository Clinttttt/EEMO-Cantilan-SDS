using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;

/// <summary>
/// Admin records/updates an NPM stall's utility assessment for a billing month. Under IA-055 a new assessment is a direct
/// approved amount (<see cref="ElecApprovedAmount"/> / <see cref="WaterApprovedAmount"/>); the reading/rate arguments are
/// accepted only to resubmit a recorded metered assessment unchanged, or as zeros for a utility that carries no charge.
/// </summary>
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
    string? Remarks,
    // IA-050: the office's approved amount, with no meter reading. When stated, that utility is DirectApproved and its
    // readings/rate arguments are ignored.
    decimal? ElecApprovedAmount = null,
    decimal? WaterApprovedAmount = null) : IRequest<Result<UtilityBillDto>>;
