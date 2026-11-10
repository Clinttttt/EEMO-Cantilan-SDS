using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>Which collected rent the Monthly Income reports on its Arrears row (IA-076): the same words IA-069 uses for a debt, judged on the day the cash came in.</summary>
public class RecoveredArrearsTests
{
    private static Stall Let(params (DateOnly From, int Years, DateOnly? EndedOn)[] terms)
    {
        var stall = Stall.Create(Guid.NewGuid(), "7", 1000m, ApplicableFees.BaseRental);
        foreach (var (from, years, endedOn) in terms)
        {
            var contract = Contract.Create(stall.Id, "Occupant", "Occupant", from, years, 1000m);
            if (endedOn is { } ended) contract.Terminate("test", ended);
            stall.Contracts.Add(contract);
        }
        return stall;
    }

    [Fact]
    public void RentPaidInALaterYear_IsArrears_EvenForATenantWhoStays()
    {
        var stall = Let((new DateOnly(2025, 1, 1), 5, null));

        Assert.True(RecoveredArrears.Qualifies(stall, 2025, 12, new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public void RentPaidInItsOwnYear_OnARunningTerm_IsNotArrears_HoweverLate()
    {
        var stall = Let((new DateOnly(2025, 1, 1), 5, null));

        Assert.False(RecoveredArrears.Qualifies(stall, 2026, 1, new DateOnly(2026, 11, 5)));
    }

    [Fact]
    public void RentPaidAfterTheTermEnded_IsArrears_EvenInTheSameYear()
    {
        var stall = Let((new DateOnly(2025, 1, 1), 5, new DateOnly(2026, 3, 31)));

        Assert.True(RecoveredArrears.Qualifies(stall, 2026, 2, new DateOnly(2026, 4, 10)));
    }

    [Fact]
    public void RentPaidOnTime_StaysRent_AfterTheTermIsLaterRenewed()
    {
        // The first term is no longer active today. That says nothing about the day this money was collected, when it was running.
        var stall = Let((new DateOnly(2025, 1, 1), 1, new DateOnly(2025, 12, 31)), (new DateOnly(2026, 1, 1), 3, null));

        Assert.False(RecoveredArrears.Qualifies(stall, 2025, 3, new DateOnly(2025, 3, 10)));
    }

    [Fact]
    public void WithNoStallToRead_OnlyTheYearDecides()
    {
        Assert.True(RecoveredArrears.Qualifies(null, 2025, 3, new DateOnly(2026, 1, 5)));
        Assert.False(RecoveredArrears.Qualifies(null, 2026, 1, new DateOnly(2026, 1, 5)));
    }
}
