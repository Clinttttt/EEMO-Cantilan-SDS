using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The lockout rule, now that the instant is passed in rather than read from the machine clock.
///
/// <para>
/// None of this could be tested before. "The account unlocks after fifteen minutes" was verifiable only by waiting fifteen
/// minutes, so the part of the rule that actually protects the office — that a lockout ENDS — went unasserted, and a change
/// making it permanent would have passed.
/// </para>
///
/// <para>
/// One rule for every kind of user. It used to be written three times, identically, on the admin, collector and payor
/// types; each test below runs against all three, because a lockout policy that differs by account type is a security hole
/// rather than an inconsistency.
/// </para>
/// </summary>
public class LockoutTests
{
    private static readonly DateTime Monday9am = new(2026, 8, 10, 1, 0, 0, DateTimeKind.Utc);   // 9am in Cantilan

    public static TheoryData<string, BaseUser> EveryKindOfUser() => new()
    {
        { "admin", AdminUser.Create("Office Head", "head", "head@example.gov.ph", TestPasswords.Hash("Str0ng-Passw0rd!"), AdminRole.Admin) },
        { "collector", CollectorUser.Create("R. Uy", "EEMO-014", "ruy", null, null, TestPasswords.Hash("Str0ng-Passw0rd!")) },
        { "payor", PayorUser.Create("Vendor", "09171234567", TestPasswords.Hash("Str0ng-Passw0rd!")) },
    };

    [Theory]
    [MemberData(nameof(EveryKindOfUser))]
    public void OneAttemptShortOfTheLimitIsNotALockout(string kind, BaseUser user)
    {
        for (var i = 0; i < DomainRules.MaxFailedLoginAttempts - 1; i++)
            user.RecordFailedLogin(Monday9am);

        Assert.False(user.IsLockedOut(Monday9am), $"{kind} was locked out early");
    }

    [Theory]
    [MemberData(nameof(EveryKindOfUser))]
    public void TheLimitLocksTheAccount(string kind, BaseUser user)
    {
        for (var i = 0; i < DomainRules.MaxFailedLoginAttempts; i++)
            user.RecordFailedLogin(Monday9am);

        Assert.True(user.IsLockedOut(Monday9am), $"{kind} was not locked out at the limit");
    }

    [Theory]
    [MemberData(nameof(EveryKindOfUser))]
    public void TheLockoutLiftsOnceItsWindowHasElapsed(string kind, BaseUser user)
    {
        for (var i = 0; i < DomainRules.MaxFailedLoginAttempts; i++)
            user.RecordFailedLogin(Monday9am);

        var window = TimeSpan.FromMinutes(DomainRules.LockoutMinutes);

        // Still locked a second before the window closes, and free a second after. A vendor at the counter is not asked to
        // come back tomorrow because they mistyped a password this morning.
        Assert.True(user.IsLockedOut(Monday9am.Add(window).AddSeconds(-1)), $"{kind} unlocked too early");
        Assert.False(user.IsLockedOut(Monday9am.Add(window).AddSeconds(1)), $"{kind} stayed locked after the window");
    }

    [Theory]
    [MemberData(nameof(EveryKindOfUser))]
    public void ALapsedLockoutStillRecordsThatItHappened(string kind, BaseUser user)
    {
        // The stored instant is evidence for the audit trail, so it stays on the row; only the ANSWER changes with time.
        for (var i = 0; i < DomainRules.MaxFailedLoginAttempts; i++)
            user.RecordFailedLogin(Monday9am);

        var afterwards = Monday9am.AddMinutes(DomainRules.LockoutMinutes + 1);

        Assert.False(user.IsLockedOut(afterwards), $"{kind} stayed locked");
        Assert.NotNull(user.LockedUntil);
        Assert.Equal(DomainRules.MaxFailedLoginAttempts, user.FailedAttempts);
    }

    [Fact]
    public void TheRuleIsWrittenOnce()
    {
        // Declared on BaseUser and inherited, not redeclared per user type.
        foreach (var type in new[] { typeof(AdminUser), typeof(CollectorUser), typeof(PayorUser) })
        {
            Assert.Null(type.GetMethod(nameof(BaseUser.IsLockedOut), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));
            Assert.Null(type.GetMethod(nameof(BaseUser.RecordFailedLogin), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));
        }
    }
}
