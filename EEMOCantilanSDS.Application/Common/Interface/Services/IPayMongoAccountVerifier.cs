namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Asks PayMongo whether an LGU's own account credentials actually work, using a key that has been SUPPLIED rather than
/// one already stored.
///
/// <para>
/// Separate from <see cref="IPaymentGateway"/> on purpose. The gateway transacts for the current tenant and resolves that
/// tenant's stored credentials; this is a question about a key the office is in the middle of entering, before anything is
/// saved. Mixing the two would mean a settings screen could only test what was already committed.
/// </para>
/// </summary>
public interface IPayMongoAccountVerifier
{
    /// <summary>
    /// Confirms the secret key is accepted by PayMongo.
    ///
    /// <para>
    /// Returns success only when PayMongo authenticates the key. A refusal is reported as a failure with a message the
    /// office can act on, and a network problem is reported as such rather than as a bad key - telling somebody their
    /// key is wrong when the internet is down is how a correct key gets replaced.
    /// </para>
    /// </summary>
    Task<Common.Result<bool>> VerifyAsync(string secretKey, CancellationToken cancellationToken = default);
}
