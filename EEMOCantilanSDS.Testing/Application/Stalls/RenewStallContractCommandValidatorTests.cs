using EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The renewal form's optional corrections are money and measurements, so the server states its own limits
/// rather than trusting the dialog that sent them.
/// </summary>
public class RenewStallContractCommandValidatorTests
{
    private static RenewStallContractCommand Command(decimal? rate = null, double? area = null) =>
        new(Guid.NewGuid(), new DateOnly(2026, 8, 1), 3, "Maria Santos", null, rate, area);

    [Fact]
    public void NoCorrections_IsValid()
    {
        var result = new RenewStallContractCommandValidator().Validate(Command());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ANegativeRent_IsRefused()
    {
        var result = new RenewStallContractCommandValidator().Validate(Command(rate: -1m));

        Assert.False(result.IsValid);
        Assert.Contains("Monthly rental cannot be negative.", result.Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public void ANegativeArea_IsRefused()
    {
        var result = new RenewStallContractCommandValidator().Validate(Command(area: -0.5));

        Assert.False(result.IsValid);
        Assert.Contains("Area cannot be negative.", result.Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public void ZeroRent_IsAllowed_BecauseASpaceCanBeLetAtNoRent()
    {
        // A daily-collected space carries no monthly rent of its own; zero is a real answer, not an error.
        var result = new RenewStallContractCommandValidator().Validate(Command(rate: 0m, area: 0d));

        Assert.True(result.IsValid);
    }
}
