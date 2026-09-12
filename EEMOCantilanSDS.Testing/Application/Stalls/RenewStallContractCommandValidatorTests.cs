using EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;
using EEMOCantilanSDS.Domain.Enums;

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

    /// <summary>
    /// A term is required of a signed contract and meaningless on an extension, so the rule follows the arrangement.
    /// </summary>
    /// <remarks>
    /// Both halves are asserted together because either alone would pass a broken rule: dropping the condition entirely would let a
    /// signed contract be renewed for nought years, and keeping it unconditional would refuse the extension this arrangement exists
    /// to record. The entity substitutes DomainRules.OpenEndedTermYears for whatever an extension passes, which is why nought is
    /// the honest thing for the form to send rather than a fabricated 1.
    /// </remarks>
    [Fact]
    public void ATermIsRequiredOfASignedContract_AndMeaninglessOnAnExtension()
    {
        var validator = new RenewStallContractCommandValidator();

        var signedWithNoTerm = validator.Validate(new RenewStallContractCommand(
            Guid.NewGuid(), new DateOnly(2026, 8, 1), 0, "Maria Santos", "Maria Santos"));

        Assert.False(signedWithNoTerm.IsValid);
        Assert.Contains("Contract duration must be at least 1 year.", signedWithNoTerm.Errors.Select(e => e.ErrorMessage));

        var extensionWithNoTerm = validator.Validate(new RenewStallContractCommand(
            Guid.NewGuid(), new DateOnly(2026, 8, 1), 0, "Maria Santos", null,
            Arrangement: OccupancyArrangement.Extension));

        Assert.True(extensionWithNoTerm.IsValid);
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
