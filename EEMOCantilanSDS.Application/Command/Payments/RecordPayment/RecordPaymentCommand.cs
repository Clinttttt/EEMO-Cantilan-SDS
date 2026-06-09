using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using System;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayment;

public record RecordPaymentCommand(
    Guid StallId,
    int Year,
    int Month,
    PaymentStatus Status,
    decimal? PartialAmount,
    string? Remarks,
    string? ORNumber = null
) : IRequest<Result<bool>>;
