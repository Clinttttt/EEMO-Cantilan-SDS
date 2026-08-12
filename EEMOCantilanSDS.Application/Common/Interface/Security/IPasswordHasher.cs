namespace EEMOCantilanSDS.Application.Common.Interface.Security;

/// <summary>
/// What checking a password produced.
///
/// <para>Application's own type rather than ASP.NET Identity's, so nothing above Infrastructure has to know which library
/// hashes passwords. The distinction that matters to a caller is whether the password was right — and, separately, whether
/// the stored hash was made by an older scheme and should be written again while we happen to have the plaintext.</para>
/// </summary>
public enum PasswordCheck
{
    /// <summary>Wrong password. Say nothing more than that to the caller.</summary>
    Failed = 0,

    /// <summary>Right password.</summary>
    Succeeded = 1,

    /// <summary>
    /// Right password, but the stored hash uses older parameters than the current ones. The account can be re-hashed
    /// now, while the plaintext is in hand; nothing breaks if it is not.
    /// </summary>
    SucceededRehashNeeded = 2,
}

/// <summary>
/// Hashes and checks passwords.
///
/// <para>
/// The one place that knows HOW a password is stored. Six handlers used to construct a concrete ASP.NET Identity hasher
/// inline, which meant the algorithm was decided in six places and Application had to reference an ASP.NET package to
/// answer "is this password right".
/// </para>
///
/// <para>
/// The format is unchanged and must stay so: an Identity password hash carries its own algorithm, salt and iteration
/// count, so a hash written before this seam existed verifies through it. Any implementation that cannot verify those
/// hashes would lock every existing user out on deployment.
/// </para>
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password for storage.</summary>
    string Hash(string password);

    /// <summary>Checks a plaintext password against a stored hash.</summary>
    PasswordCheck Check(string hash, string password);
}
