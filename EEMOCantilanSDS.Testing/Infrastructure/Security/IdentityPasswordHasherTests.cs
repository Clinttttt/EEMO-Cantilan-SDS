using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Security;

namespace EEMOCantilanSDS.Testing.Infrastructure.Security;

/// <summary>
/// The password hasher behind the Application port.
///
/// <para>
/// Six handlers used to construct ASP.NET Identity's hasher inline, which decided the algorithm in six places and made
/// Application depend on an ASP.NET package to answer "is this password right". The port moved that decision to one
/// place — and the ONE thing that must not change while doing it is the stored format. Every account in the database
/// holds a hash written by the old call sites; a hasher that cannot verify those locks out every user on deployment,
/// with no error until someone tries to sign in.
/// </para>
///
/// <para>These tests exist for that property specifically, not for the algorithm's own correctness, which is Identity's.</para>
/// </summary>
public class IdentityPasswordHasherTests
{
    private static readonly IPasswordHasher Hasher = new IdentityPasswordHasher();

    [Fact]
    public void AHashWrittenByADomainEntityStillVerifies()
    {
        // The load-bearing case. AdminUser.Create hashes inline with PasswordHasher<BaseUser>, exactly as the six handlers
        // used to, and every account in production was written that way. If the port's hasher disagreed with it, the
        // office would be locked out of its own system by a refactor.
        var admin = AdminUser.Create("Head", "head", "head@eemo.gov", "Secret123!", AdminRole.SuperAdmin);

        Assert.Equal(PasswordCheck.Succeeded, Hasher.Check(admin.PasswordHash, "Secret123!"));
        Assert.Equal(PasswordCheck.Failed, Hasher.Check(admin.PasswordHash, "Secret123"));
    }

    [Fact]
    public void AHashWrittenByACollectorEntityStillVerifies()
    {
        var collector = CollectorUser.Create("Ana Cruz", "acruz", "acruz@eemo.gov", "0917000000", "EMP-001", "Secret123!");

        Assert.Equal(PasswordCheck.Succeeded, Hasher.Check(collector.PasswordHash, "Secret123!"));
        Assert.Equal(PasswordCheck.Failed, Hasher.Check(collector.PasswordHash, "wrong"));
    }

    [Fact]
    public void AHashItWritesItselfVerifies()
    {
        var hash = Hasher.Hash("Secret123!");

        Assert.NotEqual("Secret123!", hash);
        Assert.Equal(PasswordCheck.Succeeded, Hasher.Check(hash, "Secret123!"));
        Assert.Equal(PasswordCheck.Failed, Hasher.Check(hash, "secret123!"));   // case matters
    }

    [Fact]
    public void TwoHashesOfOnePasswordDiffer()
    {
        // Salted per hash, so two accounts with the same password do not look the same in the database - and a stolen
        // table cannot be attacked once for everybody.
        Assert.NotEqual(Hasher.Hash("Secret123!"), Hasher.Hash("Secret123!"));
    }

    [Fact]
    public void AWrongPasswordIsRefusedRatherThanThrowing()
    {
        // A malformed or empty stored hash must read as "wrong password", not as an exception: a login endpoint that
        // throws on bad input tells an attacker something a 401 does not.
        Assert.Equal(PasswordCheck.Failed, Hasher.Check("not-a-hash", "Secret123!"));
        Assert.Equal(PasswordCheck.Failed, Hasher.Check(string.Empty, "Secret123!"));
    }
}
