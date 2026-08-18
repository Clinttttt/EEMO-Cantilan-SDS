namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// The PayMongo credentials to use for a given tenant's online payments, resolved per request.
///
/// <para>
/// Three states, and the third is the one that matters: an LGU with its own configured account uses its keys, so its
/// revenue settles to its own account; the DEFAULT municipality uses the global configuration, because that is its
/// account; and any other LGU that has not configured one has <see cref="None"/> - no credentials, no online payments.
/// </para>
///
/// <para>
/// The global configuration used to be handed to every tenant as a default. It is not a platform account, it is one
/// LGU's merchant account, so that quietly pointed another municipality's collections at it.
/// </para>
/// </summary>
public sealed record PayMongoCredentials(string SecretKey, string? PublicKey, string? WebhookSecret)
{
    /// <summary>No account. Online payments cannot be taken, and nothing may be sent to PayMongo on this LGU's behalf.</summary>
    public static PayMongoCredentials None { get; } = new(string.Empty, null, null);

    /// <summary>
    /// Whether these credentials can actually transact. A blank secret key cannot, and must never be sent as an
    /// Authorization header - the request would either fail as unauthenticated or, far worse, succeed against someone
    /// else's account.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}
