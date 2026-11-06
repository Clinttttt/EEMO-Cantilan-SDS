using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A month with no record is owed at the rate its term agreed (IA-077). The office's statement, the ledger, the collector's list and the vendor register all
/// read the same space, so none of them may still state the space's own rate where a term says otherwise.
/// </summary>
public class TermRateIsOneFigureTests : RepositoryTestBase
{
    private const decimal SpaceRate = 1000m;
    private const decimal TermRate = 800m;

    private static DateOnly Today => PhilippineTime.Today;
    private static DateOnly ThisMonth => new(Today.Year, Today.Month, 1);

    private async Task<(EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext Context, Stall Stall)> SeedAsync()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(facility.Id, "7", SpaceRate, ApplicableFees.BaseRental);
        context.AddRange(facility, stall, Contract.Create(stall.Id, "Occupant", "Occupant", ThisMonth.AddMonths(-3), 3, TermRate));
        await context.SaveChangesAsync();
        return (context, stall);
    }

    [Fact]
    public async Task TheStatement_StatesTheTermsRate()
    {
        var (context, stall) = await SeedAsync();

        var statement = await new FacilityReportsRepository(context).GetStallStatementAsync(stall.Id, CancellationToken.None);

        Assert.All(statement!.UnpaidMonths, m => Assert.Equal(TermRate, m.Unpaid));
        Assert.Equal(TermRate, statement.CurrentBalance);
    }

    [Fact]
    public async Task TheLedgerSummary_StatesTheTermsRate()
    {
        var (context, stall) = await SeedAsync();

        var summary = await new PaymentRepository(context).GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        Assert.Equal(summary.MonthsUnpaid * TermRate, summary.TotalOutstanding);
    }

    [Fact]
    public async Task TheCollectorsList_StatesTheTermsRate()
    {
        var (context, _) = await SeedAsync();

        var list = await new StallRepository(context).GetMobileMonthlyCollectionAsync(FacilityCode.TCC, Today.Year, Today.Month, Today, CancellationToken.None);

        var row = Assert.Single(list.Stalls);
        Assert.Equal((TermRate, TermRate), (row.MonthlyRate, row.Balance));
    }

    [Fact]
    public async Task TheVendorRegister_TargetsTheTermsRate()
    {
        var (context, _) = await SeedAsync();

        var registry = await new VendorRepository(context).GetVendorRegistryAsync(Today.Year, Today.Month, CancellationToken.None);

        Assert.Equal(TermRate, registry.MonthlyTarget);
    }
}
