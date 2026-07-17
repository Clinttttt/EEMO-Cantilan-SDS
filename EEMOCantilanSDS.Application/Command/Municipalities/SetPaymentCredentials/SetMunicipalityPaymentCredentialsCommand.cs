using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials;

/// <summary>
/// Lets an LGU Head set (or clear) their municipality's own PayMongo account so its online payments settle
/// to that account. Scoped to the caller's municipality via their token. An empty <paramref name="SecretKey"/>
/// clears the credentials, reverting the LGU to the platform's default account. The secret + webhook secret
/// are encrypted at rest by the handler; the public key is stored plain.
/// </summary>
public record SetMunicipalityPaymentCredentialsCommand(string? SecretKey, string? PublicKey, string? WebhookSecret)
    : IRequest<Result<bool>>;
