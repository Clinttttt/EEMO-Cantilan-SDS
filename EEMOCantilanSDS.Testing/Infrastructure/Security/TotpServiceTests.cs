using System;
using System.Text;
using EEMOCantilanSDS.Infrastructure.Security;
using Xunit;

namespace EEMOCantilanSDS.Testing.Infrastructure.Security;

/// <summary>
/// Conformance tests for the hand-written TOTP engine.
/// <para>
/// The first block uses the OFFICIAL RFC 6238 Appendix B test vectors (SHA-1, 8 digits, T0=0, step=30) —
/// if these pass, the implementation is standard-conformant and Google/Microsoft Authenticator will agree
/// with it. The remaining tests pin the behaviour we depend on: base32 round-tripping, drift tolerance, and
/// replay rejection.
/// </para>
/// </summary>
public class TotpServiceTests
{
    // RFC 6238 uses the ASCII seed "12345678901234567890" for the SHA-1 vectors.
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");

    /// <summary>
    /// RFC 6238 Appendix B, SHA-1 rows. The RFC prints 8-digit codes; this engine emits 6, so each expected
    /// value is the RFC code's last 6 digits (truncation is the same operation, only the modulus differs).
    /// </summary>
    [Theory]
    [InlineData(59L, "287082")]           // RFC: 94287082
    [InlineData(1111111109L, "081804")]   // RFC: 07081804
    [InlineData(1111111111L, "050471")]   // RFC: 14050471
    [InlineData(1234567890L, "005924")]   // RFC: 89005924
    [InlineData(2000000000L, "279037")]   // RFC: 69279037
    [InlineData(20000000000L, "353130")]  // RFC: 65353130
    public void ComputeCode_MatchesRfc6238Vectors(long unixTime, string expectedSixDigits)
    {
        var step = unixTime / 30;

        var code = TotpService.ComputeCode(RfcKey, step);

        Assert.Equal(expectedSixDigits, code);
    }

    [Fact]
    public void Base32_RoundTrips()
    {
        var original = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF, 0x2A, 0x99, 0x10, 0x44, 0xC3 };

        var encoded = TotpService.ToBase32(original);
        var decoded = TotpService.FromBase32(encoded);

        Assert.Equal(original, decoded);
        // Base32 output must be usable in an otpauth URI without escaping.
        Assert.Matches("^[A-Z2-7]+$", encoded);
    }

    [Fact]
    public void GenerateSecret_Is160Bits_AndUnique()
    {
        var sut = new TotpService();

        var a = sut.GenerateSecret();
        var b = sut.GenerateSecret();

        Assert.Equal(20, TotpService.FromBase32(a).Length);   // 160-bit
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ProvisioningUri_IsStandardOtpauthFormat()
    {
        var sut = new TotpService();
        var secret = sut.GenerateSecret();

        var uri = sut.BuildProvisioningUri(secret, "EEMO Cantilan", "head2");

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains($"secret={secret}", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
        Assert.Contains("issuer=EEMO%20Cantilan", uri);
    }

    // ── Validation behaviour ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryValidate_AcceptsCurrentCode()
    {
        var sut = new TotpService();
        var secret = sut.GenerateSecret();
        var code = TotpService.ComputeCode(TotpService.FromBase32(secret), sut.CurrentStep());

        Assert.True(sut.TryValidate(secret, code, minimumStep: null, out var matched));
        Assert.Equal(sut.CurrentStep(), matched);
    }

    [Fact]
    public void TryValidate_AcceptsOneStepOfDrift_BothDirections()
    {
        var sut = new TotpService();
        var secret = sut.GenerateSecret();
        var key = TotpService.FromBase32(secret);
        var now = sut.CurrentStep();

        Assert.True(sut.TryValidate(secret, TotpService.ComputeCode(key, now - 1), null, out _));
        Assert.True(sut.TryValidate(secret, TotpService.ComputeCode(key, now + 1), null, out _));
    }

    [Fact]
    public void TryValidate_RejectsCodeBeyondDriftWindow()
    {
        var sut = new TotpService();
        var secret = sut.GenerateSecret();
        var key = TotpService.FromBase32(secret);
        var now = sut.CurrentStep();

        Assert.False(sut.TryValidate(secret, TotpService.ComputeCode(key, now - 5), null, out _));
        Assert.False(sut.TryValidate(secret, TotpService.ComputeCode(key, now + 5), null, out _));
    }

    /// <summary>
    /// A code seen by an attacker must not work twice inside its own 30-second window: once a step is
    /// consumed, that step and everything before it is refused.
    /// </summary>
    [Fact]
    public void TryValidate_RejectsReplayOfAConsumedStep()
    {
        var sut = new TotpService();
        var secret = sut.GenerateSecret();
        var key = TotpService.FromBase32(secret);
        var now = sut.CurrentStep();
        var code = TotpService.ComputeCode(key, now);

        Assert.True(sut.TryValidate(secret, code, minimumStep: null, out var matched));
        Assert.Equal(now, matched);

        // Same code, now that the step has been recorded as used.
        Assert.False(sut.TryValidate(secret, code, minimumStep: matched, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]      // too short
    [InlineData("1234567")]    // too long
    [InlineData("abcdef")]     // not digits
    public void TryValidate_RejectsMalformedInput(string code)
    {
        var sut = new TotpService();
        var secret = sut.GenerateSecret();

        Assert.False(sut.TryValidate(secret, code, null, out _));
    }

    [Fact]
    public void TryValidate_RejectsGarbageSecret_WithoutThrowing()
    {
        var sut = new TotpService();

        Assert.False(sut.TryValidate("not-base32!!", "123456", null, out _));
        Assert.False(sut.TryValidate("", "123456", null, out _));
    }

    [Fact]
    public void QrCode_ProducesScannablePngDataUri()
    {
        var sut = new QrCodeGenerator();

        var dataUri = sut.ToPngDataUri("otpauth://totp/EEMO:head2?secret=ABCDEFGHIJKLMNOP");

        Assert.StartsWith("data:image/png;base64,", dataUri);
        var bytes = Convert.FromBase64String(dataUri["data:image/png;base64,".Length..]);
        // PNG magic number — proves a real image was produced, not an empty/placeholder buffer.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        Assert.Equal(string.Empty, sut.ToPngDataUri("  "));
    }
}
