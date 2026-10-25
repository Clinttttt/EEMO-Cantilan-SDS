using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

/// <summary>A renewed space read term by term: every period in exactly one term, a later payment staying with the term it settles.</summary>
public sealed class SpaceTermSplitTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static ObligationAccount Account()
    {
        var account = ObligationAccount.Create(Tenant, ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "7", null, null, new DateOnly(2023, 1, 1), "test", actualOccupant: "Ana Reyes");
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "KM-1");
        account.SetLeaseDetails("Ana Reyes", new DateOnly(2023, 1, 1), 1, null, null);
        return account;
    }

    private static SpaceContractTerm Term(ObligationAccount account, DateOnly commencement, int years = 1) =>
        SpaceContractTerm.Create(Tenant, account.Id, Guid.NewGuid(), null, commencement, years, "Ana Reyes", "KM-2", "Res. 1", Guid.NewGuid(), Guid.NewGuid(), "head", DateTime.UtcNow);

    private static ObligationQuoteDto Month(ObligationAccount account, int year, int month, decimal settled) =>
        new(account.Id, Guid.NewGuid(), account.Kind, "Kanmanggay", "7", new DateOnly(year, month, 1), 1000m, settled, 1000m - settled, Guid.Empty, null, null, true);

    [Fact]
    public void ANeverRenewedAccount_IsNotSplit()
    {
        var account = Account();
        Assert.Null(SpaceTermSplit.Split(account, [], [Month(account, 2023, 5, 0m)]));
        Assert.Null(SpaceTermSplit.CurrentTermStart([]));
    }

    [Fact]
    public void EachPeriodBelongsToTheTermInForceAtItsStart_LapsedMonthsStayWithTheTermThatRanOut_AndPaymentsStayWithThePeriodTheySettle()
    {
        var account = Account();                                                 // first term 2023-01-01 .. 2023-12-31
        var renewal = Term(account, new DateOnly(2024, 3, 15));                 // renewed late, mid-month
        var quotes = new[]
        {
            Month(account, 2023, 12, 0m),
            Month(account, 2024, 1, 400m),                                      // lapsed month, part-paid AFTER the renewal: still the earlier term's
            Month(account, 2024, 3, 0m),                                        // the renewal month began before it: earlier term
            Month(account, 2024, 4, 1000m),                                     // first full month of the new term, paid
            Month(account, 2024, 5, 0m)
        };

        var terms = SpaceTermSplit.Split(account, SpaceTermSplit.Ordered([renewal]), quotes)!;

        Assert.Equal(2, terms.Count);
        Assert.Equal((new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31), new DateOnly(2024, 3, 14), false),
            (terms[0].Commencement, terms[0].LastDay!.Value, terms[0].RenewedOn!.Value, terms[0].Current));
        Assert.Equal((3000m, 400m, 2600m), (terms[0].Assessed, terms[0].Collected, terms[0].Outstanding));
        Assert.Equal((2000m, 1000m, 1000m, true), (terms[1].Assessed, terms[1].Collected, terms[1].Outstanding, terms[1].Current));
        Assert.Equal((quotes.Sum(q => q.AssessedAmount), quotes.Sum(q => q.SettledAmount), quotes.Sum(q => q.OutstandingAmount)),
            (terms.Sum(t => t.Assessed), terms.Sum(t => t.Collected), terms.Sum(t => t.Outstanding)));
        Assert.Equal(new DateOnly(2024, 3, 15), SpaceTermSplit.CurrentTermStart(SpaceTermSplit.Ordered([renewal])));
    }
}
