using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Security;
using EEMOCantilanSDS.Application.Queries.Auth.GetMfaStatus;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Auth;

/// <summary>
/// Two-factor enrollment (Slice 1: opt-in, sign-in NOT yet gated).
/// <para>
/// These pin the security properties: the password is re-checked before any change, the secret is stored
/// ENCRYPTED (never plaintext), enrollment is inert until a valid code confirms it, recovery codes are
/// single-use and only ever hashed at rest, turning MFA off requires the second factor, and regenerating
/// codes invalidates the old set.
/// </para>
/// </summary>
public class MfaEnrollmentTests
{
    private const string Password = "Secret123!";

    /// <summary>Reversible stand-in for AES-GCM protection — lets tests assert the stored value isn't plaintext.</summary>
    private sealed class FakeProtector : ICredentialProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext.StartsWith("enc:") ? ciphertext[4..] : ciphertext;
    }

    private static (MfaCommandHandlers handlers, AdminUser user, Mock<IUnitOfWork> uow) Build(AdminUser? existing = null)
    {
        var user = existing ?? AdminUser.Create("Head", "head", "head@eemo.gov", Password, AdminRole.SuperAdmin);

        var repo = new Mock<IAdminRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(user.Id);
        currentUser.SetupGet(c => c.Username).Returns(user.Username);
        currentUser.SetupGet(c => c.MunicipalityCode).Returns("CANTILAN");

        var uow = new Mock<IUnitOfWork>();

        var handlers = new MfaCommandHandlers(
            repo.Object, currentUser.Object, new FakeProtector(), new TotpService(), new QrCodeGenerator(), uow.Object);

        return (handlers, user, uow);
    }

    private static string CodeFor(AdminUser user, long? stepOffset = null)
    {
        var secret = new FakeProtector().Unprotect(user.MfaSecretCipher!);
        var totp = new TotpService();
        var step = totp.CurrentStep() + (stepOffset ?? 0);
        return TotpService.ComputeCode(TotpService.FromBase32(secret), step);
    }

    // ── Enrollment ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Begin_WithCorrectPassword_IssuesSecretAndQr_ButDoesNotEnableYet()
    {
        var (handlers, user, _) = Build();

        var result = await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.ManualKey));
        Assert.StartsWith("otpauth://totp/", result.Value.ProvisioningUri);
        Assert.StartsWith("data:image/png;base64,", result.Value.QrCodeDataUri);
        Assert.Contains("issuer=StallTrack%20CANTILAN", result.Value.ProvisioningUri);

        // Pending, not active — sign-in is unaffected until confirmation.
        Assert.False(user.MfaEnabled);
        Assert.True(user.HasPendingMfaEnrollment);
        // The protector was applied: what is persisted is not the raw secret.
        Assert.NotNull(user.MfaSecretCipher);
        Assert.NotEqual(result.Value.ManualKey, user.MfaSecretCipher);
        Assert.StartsWith("enc:", user.MfaSecretCipher);
    }

    /// <summary>
    /// The fake protector above only proves the call happens. This proves the REAL one (AES-256-GCM, the same
    /// implementation used in production for gateway credentials) leaves no trace of the secret at rest.
    /// </summary>
    [Fact]
    public void RealProtector_LeavesNoPlaintextSecretAtRest()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = Convert.ToBase64String(new byte[32])   // test-only key
            })
            .Build();
        var protector = new AesCredentialProtector(config);
        var secret = new TotpService().GenerateSecret();

        var cipher = protector.Protect(secret);

        Assert.DoesNotContain(secret, cipher);
        Assert.NotEqual(secret, cipher);
        Assert.Equal(secret, protector.Unprotect(cipher));   // still reversible for validation
    }

    [Fact]
    public async Task Begin_WithWrongPassword_IsRejected_AndChangesNothing()
    {
        var (handlers, user, uow) = Build();

        var result = await handlers.Handle(new BeginMfaEnrollmentCommand("WrongPassword!"), default);

        Assert.False(result.IsSuccess);
        Assert.Null(user.MfaSecretCipher);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_WithValidCode_EnablesMfa_AndReturnsRecoveryCodesOnce()
    {
        var (handlers, user, _) = Build();
        await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);

        var result = await handlers.Handle(new ConfirmMfaEnrollmentCommand(CodeFor(user)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecoveryCodes.SetSize, result.Value!.Codes.Count);
        Assert.True(user.MfaEnabled);
        Assert.NotNull(user.MfaEnrolledAt);
        Assert.Equal(RecoveryCodes.SetSize, user.MfaRecoveryCodesRemaining);

        // Only HASHES are stored — no plaintext code may appear in the persisted value.
        foreach (var code in result.Value.Codes)
            Assert.DoesNotContain(code, user.MfaRecoveryCodeHashes!);
    }

    [Fact]
    public async Task Confirm_WithWrongCode_DoesNotEnable()
    {
        var (handlers, user, _) = Build();
        await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);

        var result = await handlers.Handle(new ConfirmMfaEnrollmentCommand("000000"), default);

        Assert.False(result.IsSuccess);
        Assert.False(user.MfaEnabled);
        Assert.True(user.HasPendingMfaEnrollment);   // still pending, can retry
    }

    [Fact]
    public async Task Confirm_WithoutStartingEnrollment_IsRejected()
    {
        var (handlers, _, _) = Build();

        var result = await handlers.Handle(new ConfirmMfaEnrollmentCommand("123456"), default);

        Assert.False(result.IsSuccess);
    }

    /// <summary>The code that activated MFA must not be replayable immediately afterwards.</summary>
    [Fact]
    public async Task Confirm_RecordsStep_SoTheSameCodeCannotBeReused()
    {
        var (handlers, user, _) = Build();
        await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);
        var code = CodeFor(user);
        await handlers.Handle(new ConfirmMfaEnrollmentCommand(code), default);

        Assert.Equal(new TotpService().CurrentStep(), user.MfaLastUsedStep);

        // Reusing that same code to disable MFA must fail (step already consumed).
        var disable = await handlers.Handle(new DisableMfaCommand(Password, code), default);
        Assert.False(disable.IsSuccess);
        Assert.True(user.MfaEnabled);
    }

    [Fact]
    public async Task Begin_WhenAlreadyEnabled_IsRejected()
    {
        var (handlers, user, _) = Build();
        await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);
        await handlers.Handle(new ConfirmMfaEnrollmentCommand(CodeFor(user)), default);

        var again = await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);

        Assert.False(again.IsSuccess);
        Assert.True(user.MfaEnabled);
    }

    // ── Disable ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Disable_WithPasswordAndFreshCode_ClearsEverything()
    {
        var (handlers, user, _) = Build();
        await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);
        await handlers.Handle(new ConfirmMfaEnrollmentCommand(CodeFor(user)), default);

        // A later step, so it is not the already-consumed confirmation code.
        var result = await handlers.Handle(new DisableMfaCommand(Password, CodeFor(user, stepOffset: 1)), default);

        Assert.True(result.IsSuccess);
        Assert.False(user.MfaEnabled);
        Assert.Null(user.MfaSecretCipher);
        Assert.Null(user.MfaRecoveryCodeHashes);
        Assert.Null(user.MfaLastUsedStep);
    }

    [Fact]
    public async Task Disable_WithoutSecondFactor_IsRejected()
    {
        var (handlers, user, _) = Build();
        await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);
        await handlers.Handle(new ConfirmMfaEnrollmentCommand(CodeFor(user)), default);

        var result = await handlers.Handle(new DisableMfaCommand(Password, "000000"), default);

        Assert.False(result.IsSuccess);
        Assert.True(user.MfaEnabled);          // still protected
    }

    [Fact]
    public async Task Disable_AcceptsARecoveryCode_AndConsumesIt()
    {
        var (handlers, user, _) = Build();
        await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);
        var codes = (await handlers.Handle(new ConfirmMfaEnrollmentCommand(CodeFor(user)), default)).Value!.Codes;

        var result = await handlers.Handle(new DisableMfaCommand(Password, codes[0]), default);

        Assert.True(result.IsSuccess);
        Assert.False(user.MfaEnabled);
    }

    // ── Recovery codes ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Regenerate_IssuesANewSet_AndInvalidatesTheOldOnes()
    {
        var (handlers, user, _) = Build();
        await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);
        var original = (await handlers.Handle(new ConfirmMfaEnrollmentCommand(CodeFor(user)), default)).Value!.Codes;

        var regenerated = await handlers.Handle(new RegenerateRecoveryCodesCommand(Password), default);

        Assert.True(regenerated.IsSuccess);
        Assert.Equal(RecoveryCodes.SetSize, regenerated.Value!.Codes.Count);
        Assert.Empty(regenerated.Value.Codes.Intersect(original));

        // An old code no longer works.
        Assert.False(user.TryConsumeRecoveryCode(RecoveryCodes.Hash(original[0])));
        // A new one does, exactly once.
        Assert.True(user.TryConsumeRecoveryCode(RecoveryCodes.Hash(regenerated.Value.Codes[0])));
        Assert.False(user.TryConsumeRecoveryCode(RecoveryCodes.Hash(regenerated.Value.Codes[0])));
    }

    [Fact]
    public void RecoveryCode_MatchIgnoresFormatting()
    {
        var (plain, hashes) = RecoveryCodes.Generate(1);
        var user = AdminUser.Create("Head", "head", "head@eemo.gov", Password, AdminRole.Admin);
        user.ReplaceRecoveryCodes(hashes);

        var messy = plain[0].ToLowerInvariant().Replace("-", " ");
        Assert.True(user.TryConsumeRecoveryCode(RecoveryCodes.Hash(messy)));
    }

    [Fact]
    public async Task Regenerate_WhenMfaOff_IsRejected()
    {
        var (handlers, _, _) = Build();

        var result = await handlers.Handle(new RegenerateRecoveryCodesCommand(Password), default);

        Assert.False(result.IsSuccess);
    }

    // ── Status ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_ReportsPendingThenEnabled()
    {
        var (handlers, user, _) = Build();

        var repo = new Mock<IAdminRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(user.Id);
        var statusHandler = new GetMfaStatusQueryHandler(repo.Object, currentUser.Object);

        var before = await statusHandler.Handle(new GetMfaStatusQuery(), default);
        Assert.False(before.Value!.Enabled);
        Assert.False(before.Value.PendingEnrollment);

        await handlers.Handle(new BeginMfaEnrollmentCommand(Password), default);
        var pending = await statusHandler.Handle(new GetMfaStatusQuery(), default);
        Assert.True(pending.Value!.PendingEnrollment);
        Assert.False(pending.Value.Enabled);

        await handlers.Handle(new ConfirmMfaEnrollmentCommand(CodeFor(user)), default);
        var enabled = await statusHandler.Handle(new GetMfaStatusQuery(), default);
        Assert.True(enabled.Value!.Enabled);
        Assert.False(enabled.Value.PendingEnrollment);
        Assert.Equal(RecoveryCodes.SetSize, enabled.Value.RecoveryCodesRemaining);
    }
}
