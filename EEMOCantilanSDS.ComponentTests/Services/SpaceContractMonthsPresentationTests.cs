using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.ComponentTests.Services;

/// <summary>What the Head reads for a month-based space term: the same last day every consumer computes, in plain wording. Yearly terms read as before.</summary>
public sealed class SpaceContractMonthsPresentationTests
{
    private static ObligationAccountDto Account(DateOnly start, int? years, int? months) => new(
        Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental, "Kanmanggay", Guid.NewGuid(), "Holder", null, null, "1", null, null, start, null, 900m, start,
        0m, 0m, 0m, OccupancyArrangement.SignedContract, "REF", "Holder", null, start, years, ContractMonths: months);

    [Fact]
    public void StatusAndLastDayUseTheMonthAwareRule()
    {
        var sixMonths = Account(new DateOnly(2020, 1, 1), 0, 6);
        Assert.Equal(new DateOnly(2020, 6, 30), RentalAccountStatus.TermLastDay(sixMonths));
        Assert.True(RentalAccountStatus.IsTermEnded(sixMonths));                                  // long past
        Assert.Equal("Term ended Jun 30, 2020", RentalAccountStatus.LabelWithDate(sixMonths));

        var yearly = Account(new DateOnly(2020, 1, 1), 3, null);
        Assert.Equal(new DateOnly(2022, 12, 31), RentalAccountStatus.TermLastDay(yearly));         // unchanged

        var running = Account(new DateOnly(2999, 1, 1), 0, 6);
        Assert.False(RentalAccountStatus.IsTermEnded(running));
        Assert.Null(RentalAccountStatus.TermLastDay(Account(new DateOnly(2020, 1, 1), null, null)));
    }

    [Theory]
    [InlineData(3, null, "3 years · expires Dec 2022")]
    [InlineData(1, null, "1 year · expires Dec 2020")]
    [InlineData(0, 6, "6 months · expires Jun 2020")]
    [InlineData(1, 6, "1 year 6 months · expires Jun 2021")]
    public void TheRegistryContractColumnStatesTheTermInPlainWords(int years, int? months, string expected)
    {
        var account = Account(new DateOnly(2020, 1, 1), years, months) with { ContractName = null };
        Assert.Equal(expected, SpaceAccountPresentation.RegistryContract(account));
    }
}
