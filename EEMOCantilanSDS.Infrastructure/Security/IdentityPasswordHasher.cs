using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.AspNetCore.Identity;

namespace EEMOCantilanSDS.Infrastructure.Security;

/// <summary>
/// ASP.NET Identity's password hasher, behind the Application port.
///
/// <para>
/// Deliberately the SAME hasher, with the same default options, that the six handlers and the user entities constructed
/// inline: <c>PasswordHasher&lt;BaseUser&gt;</c> with no configuration. An Identity hash carries its own format marker,
/// salt and iteration count, so every password already stored verifies through this class unchanged. Changing the options
/// here — or swapping the algorithm — would lock out every existing account on the next deployment, which is why the
/// choice is stated rather than left to look incidental.
/// </para>
///
/// <para>
/// The generic argument is unused by Identity's V3 format (the user object is not part of the hash), which is why the
/// entities could get away with passing <c>null!</c>. It is kept as <see cref="BaseUser"/> so this class produces byte-
/// identical output to the call sites it replaces.
/// </para>
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<BaseUser> _hasher = new();

    public HashedPassword Hash(string password) => new(_hasher.HashPassword(null!, password));

    public PasswordCheck Check(string hash, string password)
    {
        // A stored hash that is empty or malformed reads as a WRONG PASSWORD, not as an exception.
        //
        // Identity's verifier decodes the hash as base-64 and throws FormatException on anything else. The six call
        // sites this replaced passed the stored value straight in, so a row whose PasswordHash was blank or corrupted -
        // by a partial restore, a hand-edited row, or an account created down some path that never set one - turned a
        // sign-in attempt into a 500. That is both a worse experience for the owner and a signal to someone probing:
        // an error that only some accounts produce is an error that identifies them.
        if (string.IsNullOrEmpty(hash)) return PasswordCheck.Failed;

        try
        {
            return _hasher.VerifyHashedPassword(null!, hash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Succeeded,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SucceededRehashNeeded,
                _ => PasswordCheck.Failed,
            };
        }
        catch (FormatException)
        {
            return PasswordCheck.Failed;
        }
        catch (ArgumentException)
        {
            return PasswordCheck.Failed;
        }
    }
}
