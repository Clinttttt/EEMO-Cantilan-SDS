using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Security;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Password helpers for tests, using the SAME hasher production uses.
///
/// <para>
/// Domain no longer hashes or verifies: <c>BaseUser.VerifyPassword</c> is gone and the password-change methods take an
/// already-hashed value. Tests therefore need somewhere to do both, and it must be the real implementation — a test that
/// hashed differently from production would prove nothing about whether an account can actually sign in.
/// </para>
/// </summary>
public static class TestPasswords
{
    private static readonly IPasswordHasher Hasher = new IdentityPasswordHasher();

    /// <summary>Hashes a password the way production does, for handing to a domain factory or password-change method.</summary>
    public static string Hash(string password) => Hasher.Hash(password);

    /// <summary>Whether this user's stored hash accepts the password — the assertion that matters after a reset.</summary>
    public static bool Accepts(this BaseUser user, string password) =>
        Hasher.Check(user.PasswordHash, password) != PasswordCheck.Failed;
}
