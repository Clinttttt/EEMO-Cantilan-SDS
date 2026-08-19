using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials;

/// <summary>
/// Lets an LGU Head set (or clear) their municipality's own PayMongo account so its online payments settle
/// to that account. Scoped to the caller's municipality via their token. An empty <paramref name="SecretKey"/>
/// clears the credentials, leaving the LGU with no account of its own. The secret + webhook secret
/// are encrypted at rest by the handler; the public key is stored plain.
///
/// <para>
/// <paramref name="WebhookSecret"/> is optional because the handler tries to register the webhook itself and keep the
/// signing secret PayMongo returns. Supplying one is still honoured, and is never overwritten by that attempt - an office
/// that pasted its own secret meant it.
/// </para>
///
/// <para>
/// Returns what actually happened rather than a bare true: storing the key does not need PayMongo to answer, registering a
/// webhook does, and the office has to be told when only the first of those worked.
/// </para>
/// </summary>
public record SetMunicipalityPaymentCredentialsCommand(string? SecretKey, string? PublicKey, string? WebhookSecret)
    : IRequest<Result<PaymentSetupResultDto>>;
