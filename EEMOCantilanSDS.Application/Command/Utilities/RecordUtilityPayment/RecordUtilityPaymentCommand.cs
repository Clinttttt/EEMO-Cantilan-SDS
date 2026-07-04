using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityPayment;

/// <summary>
/// Records a collection against a utility bill, with electricity and water settled independently
/// (either can be Paid / Partial / Unpaid). A partial that meets/exceeds a utility's charge
/// auto-upgrades that utility to Paid. <see cref="ClientOperationId"/> is the offline idempotency key.
/// </summary>
public record RecordUtilityPaymentCommand(
    Guid BillId,
    PaymentStatus ElecStatus,
    decimal? ElecPartialAmount,
    PaymentStatus WaterStatus,
    decimal? WaterPartialAmount,
    string? ElecORNumber,
    string? WaterORNumber,
    string? Remarks,
    Guid? ClientOperationId = null) : IRequest<Result<UtilityBillDto>>;
