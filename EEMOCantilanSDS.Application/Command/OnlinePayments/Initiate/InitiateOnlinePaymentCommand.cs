using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;

/// <summary>
/// Payor initiates an online payment for one billing month of a stall. The monthly record is found or
/// created at initiation (so a current-month obligation with no record yet is payable). Full balance only.
/// <see cref="Kind"/> disambiguates NPM's payable items (daily fees vs the utility bill vs a fish day) for
/// the same stall + month; monthly-rental facilities ignore it. <see cref="Day"/> + <see cref="FishKilos"/>
/// are used only by the NPM fish-day kind (pay ONE day, self-declaring that day's kilos).
/// </summary>
public record InitiateOnlinePaymentCommand(Guid StallId, int Year, int Month, PayorPayableKind Kind = PayorPayableKind.Monthly, int? Day = null, decimal? FishKilos = null) : IRequest<Result<InitiateOnlinePaymentResultDto>>;
