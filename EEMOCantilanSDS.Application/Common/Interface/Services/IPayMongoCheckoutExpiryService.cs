using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>Expires an LGU's abandoned PayMongo checkout only after provider verification.
/// The provider refuses expiry of paid or in-process funds.</summary>
public interface IPayMongoCheckoutExpiryService
{
    Task<Result<PaymentGatewayEvent>> ExpireAndVerifyAsync(
        string checkoutId, CancellationToken cancellationToken = default);
}
