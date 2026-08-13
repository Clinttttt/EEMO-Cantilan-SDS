using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Every one-time token the office issues must stop working. Each of these windows was previously checked against the
/// machine clock, so the expiry half of each rule could only be verified by waiting it out — which no suite does, meaning a
/// token that never expired would have looked correct.
///
/// <para>
/// These are the credentials that let someone set a password or complete a sign-in without knowing the old one, so "it
/// expires" is the whole point of them.
/// </para>
/// </summary>
public class TokenExpiryTests
{
    private static readonly DateTime Issued = new(2026, 8, 10, 1, 0, 0, DateTimeKind.Utc);

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static AdminUser Admin() =>
        AdminUser.Create("Office Head", "head", "head@example.gov.ph", TestPasswords.Hash("Str0ng-Passw0rd!"), AdminRole.Admin);

    [Fact]
    public void AnActivationTokenStopsWorkingWhenItExpires()
    {
        var admin = Admin();
        admin.SetActivationToken(Hash("raw-activation"), Issued.AddHours(48));

        Assert.True(admin.IsActivationTokenValid(Hash("raw-activation"), Issued.AddHours(47)));
        Assert.False(admin.IsActivationTokenValid(Hash("raw-activation"), Issued.AddHours(49)));

        // A different token is refused even inside the window.
        Assert.False(admin.IsActivationTokenValid(Hash("someone-elses"), Issued.AddHours(1)));
    }

    [Fact]
    public void APasswordResetTokenStopsWorkingWhenItExpires()
    {
        var admin = Admin();
        admin.SetPasswordResetToken(Hash("raw-reset"), Issued.AddHours(2), Issued);

        Assert.True(admin.IsPasswordResetTokenValid(Hash("raw-reset"), Issued.AddHours(1)));
        Assert.False(admin.IsPasswordResetTokenValid(Hash("raw-reset"), Issued.AddHours(3)));
        Assert.False(admin.IsPasswordResetTokenValid(Hash("someone-elses"), Issued));
    }

    [Fact]
    public void AnMfaChallengeStopsWorkingWhenItExpires()
    {
        var admin = Admin();
        admin.SetMfaChallenge(Hash("raw-challenge"), Issued.AddMinutes(5));

        Assert.True(admin.IsMfaChallengeValid(Hash("raw-challenge"), Issued.AddMinutes(4)));
        Assert.False(admin.IsMfaChallengeValid(Hash("raw-challenge"), Issued.AddMinutes(6)));
    }

    [Fact]
    public void AnEmailVerificationTokenStopsWorkingWhenItExpires()
    {
        var admin = Admin();
        admin.SetEmailVerificationToken(Hash("raw-verify"), Issued.AddHours(24));

        Assert.True(admin.IsEmailVerificationTokenValid(Hash("raw-verify"), Issued.AddHours(23)));
        Assert.False(admin.IsEmailVerificationTokenValid(Hash("raw-verify"), Issued.AddHours(25)));
    }

    [Fact]
    public void ARefreshTokenStopsWorkingWhenItExpires()
    {
        var admin = Admin();
        admin.SetRefreshToken(Hash("raw-refresh"), Issued.AddDays(7));

        Assert.True(admin.CanRefresh(Hash("raw-refresh"), Issued.AddDays(6)));
        Assert.False(admin.CanRefresh(Hash("raw-refresh"), Issued.AddDays(8)));
        Assert.False(admin.CanRefresh(Hash("someone-elses"), Issued));
    }

    [Fact]
    public void ALockedAccountCannotRefreshItsSession()
    {
        // The refresh path had its own transcription of the lockout check. It now asks the user, so locking out and
        // refreshing can no longer disagree — otherwise an account locked at the counter keeps a live session elsewhere.
        var admin = Admin();
        admin.SetRefreshToken(Hash("raw-refresh"), Issued.AddDays(7));

        for (var i = 0; i < Domain.Constants.DomainRules.MaxFailedLoginAttempts; i++)
            admin.RecordFailedLogin(Issued);

        Assert.True(admin.IsLockedOut(Issued));
        Assert.False(admin.CanRefresh(Hash("raw-refresh"), Issued));

        // Once the lockout lapses, the still-valid refresh token works again: a lockout is a pause, not a revocation.
        var afterLockout = Issued.AddMinutes(Domain.Constants.DomainRules.LockoutMinutes + 1);
        Assert.True(admin.CanRefresh(Hash("raw-refresh"), afterLockout));
    }

    [Fact]
    public void AClearedRefreshTokenIsNotMatchedByAnEmptyOne()
    {
        // After sign-out the stored token is null. An empty presented value must not slide through on a null == null
        // comparison, which is why the check requires a stored token to exist.
        var admin = Admin();
        admin.SetRefreshToken(Hash("raw-refresh"), Issued.AddDays(7));
        admin.ClearRefreshToken();

        Assert.False(admin.CanRefresh("", Issued));
        Assert.False(admin.CanRefresh(null!, Issued));
    }
}
