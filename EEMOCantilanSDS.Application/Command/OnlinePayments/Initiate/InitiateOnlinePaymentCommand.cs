using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;

/// <summary>
/// Payor initiates an online payment for one billing month of a stall. The monthly record is found or
/// created at initiation (so a current-month obligation with no record yet is payable). Full balance only.
/// </summary>
public record InitiateOnlinePaymentCommand(Guid StallId, int Year, int Month) : IRequest<Result<InitiateOnlinePaymentResultDto>>;
