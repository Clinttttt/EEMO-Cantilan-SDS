using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.ResolveException;

/// <summary>The Head records what became of a held capture. <paramref name="Reference"/> is the OR the office issued, or the gateway's refund reference.</summary>
public record ResolveOnlinePaymentExceptionCommand(
    Guid TransactionId,
    OnlinePaymentDisposition Disposition,
    string Reference,
    string? Note = null) : IRequest<Result<bool>>;
