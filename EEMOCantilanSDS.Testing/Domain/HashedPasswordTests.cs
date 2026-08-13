using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Security;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// What <see cref="HashedPassword"/> is for.
///
/// <para>
/// Moving hashing out of the domain left the factories and password-change methods taking a string that must ALREADY be
/// hashed — indistinguishable, to the compiler, from the plaintext they used to take. A caller passing plaintext would have
/// stored it verbatim as the account's hash: the account could never sign in again, no exception would be thrown, and the
/// build would be green. The type is what makes that a compile error instead, at every one of the hundred-odd call sites.
/// </para>
///
/// <para>
/// These tests cover what the type guarantees at runtime; the compile-time guarantee is the type's existence, which is why
/// no test here passes a bare string — it would not build.
/// </para>
/// </summary>
public class HashedPasswordTests
{
    private static readonly IPasswordHasher Hasher = new IdentityPasswordHasher();

    [Fact]
    public void AnAccountCreatedWithAHashedPasswordAcceptsTheOriginalPassword()
    {
        // The round trip that matters: hash, store through the factory, then verify. If the factory stored the wrong thing —
        // the plaintext, or a re-hash of the hash — this fails.
        var admin = AdminUser.Create(
            "Office Head", "head", "head@example.gov.ph", Hasher.Hash("Str0ng-Passw0rd!"), AdminRole.Admin);

        Assert.Equal(PasswordCheck.Succeeded, Hasher.Check(admin.PasswordHash, "Str0ng-Passw0rd!"));
        Assert.Equal(PasswordCheck.Failed, Hasher.Check(admin.PasswordHash, "wrong"));
    }

    [Fact]
    public void TheStoredValueIsNeverThePlaintext()
    {
        var collector = CollectorUser.Create(
            "R. Uy", "EEMO-014", "ruy", null, null, Hasher.Hash("Str0ng-Passw0rd!"));

        Assert.NotEqual("Str0ng-Passw0rd!", collector.PasswordHash);
        Assert.DoesNotContain("Str0ng-Passw0rd!", collector.PasswordHash);
    }

    [Fact]
    public void EveryKindOfAccountStoresARealHash()
    {
        var payor = PayorUser.Create("Vendor", "09171234567", Hasher.Hash("Str0ng-Passw0rd!"));

        Assert.Equal(PasswordCheck.Succeeded, Hasher.Check(payor.PasswordHash, "Str0ng-Passw0rd!"));
    }

    [Fact]
    public void APasswordChangeAlsoStoresAHashAndTheOldOneStopsWorking()
    {
        var admin = AdminUser.Create(
            "Office Head", "head", "head@example.gov.ph", Hasher.Hash("Old-Passw0rd!"), AdminRole.Admin);

        admin.ResetPassword(Hasher.Hash("New-Passw0rd!"), "Head");

        Assert.Equal(PasswordCheck.Succeeded, Hasher.Check(admin.PasswordHash, "New-Passw0rd!"));
        Assert.Equal(PasswordCheck.Failed, Hasher.Check(admin.PasswordHash, "Old-Passw0rd!"));
    }

    [Fact]
    public void AnEmptyHashIsRefusedRatherThanStored()
    {
        // An empty hash accepts nothing, so it is a bug rather than a state: better to fail where it is constructed than to
        // write a row whose owner can never sign in.
        Assert.Throws<ArgumentException>(() => new HashedPassword(""));
        Assert.Throws<ArgumentException>(() => new HashedPassword("   "));
        Assert.Throws<ArgumentException>(() => new HashedPassword(null!));
    }

    [Fact]
    public void TheHashIsSaltedSoTwoAccountsWithTheSamePasswordDifferOnDisk()
    {
        var one = AdminUser.Create("A", "a", "a@example.gov.ph", Hasher.Hash("Same-Passw0rd!"), AdminRole.Admin);
        var two = AdminUser.Create("B", "b", "b@example.gov.ph", Hasher.Hash("Same-Passw0rd!"), AdminRole.Admin);

        Assert.NotEqual(one.PasswordHash, two.PasswordHash);
        Assert.Equal(PasswordCheck.Succeeded, Hasher.Check(one.PasswordHash, "Same-Passw0rd!"));
        Assert.Equal(PasswordCheck.Succeeded, Hasher.Check(two.PasswordHash, "Same-Passw0rd!"));
    }
}
