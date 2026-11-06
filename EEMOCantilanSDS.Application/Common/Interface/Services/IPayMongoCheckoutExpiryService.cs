using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>Expires one of an LGU's PayMongo checkouts, then re-reads it. The returned event says what is true now:
/// the provider refuses to expire paid or in-process funds, so only an Expired result means the link is dead.</summary>
public interface IPayMongoCheckoutExpiryService
{
    Task<Result<PaymentGatewayEvent>> ExpireAndVerifyAsync(
        string checkoutId, CancellationToken cancellationToken = default);
}
