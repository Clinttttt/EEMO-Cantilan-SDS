using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayment;

public record RecordPaymentCommand(
    Guid StallId,
    int Year,
    int Month,
    PaymentStatus Status,
    decimal? PartialAmount,
    string? Remarks
) : IRequest<Result<bool>>;
