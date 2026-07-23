using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.CreateFirstAdmin;

namespace EEMOCantilanSDS.Testing.Application.Auth;

/// <summary>
/// Regression: the first SuperAdmin/Head password must meet the same complexity as the other privileged
/// account flows (min 8 chars + a letter + a digit) — it previously required only a minimum length, which
/// left the highest-privilege account guarded by the weakest policy.
/// </summary>
public class CreateFirstAdminCommandValidatorTests
{
    private static CreateFirstAdminCommand Command(string password) =>
        new(FullName: "System Head",
            Username: "head",
            Email: "head@example.gov.ph",
            Password: password);

    private readonly CreateFirstAdminCommandValidator _validator = new();

    [Theory]
    [InlineData("Str0ngPass")]   // letters + digit, >= 8
    [InlineData("abc12345")]     // letters + digits, exactly 8
    public async Task ValidPassword_Passes(string password)
    {
        var result = await _validator.ValidateAsync(Command(password));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]             // empty
    [InlineData("Ab1")]          // too short
    [InlineData("password")]     // no digit
    [InlineData("12345678")]     // no letter
    public async Task WeakPassword_FailsOnPassword(string password)
    {
        var result = await _validator.ValidateAsync(Command(password));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateFirstAdminCommand.Password));
    }
}
