using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// A space or lot account opens the stall-holder details drawer: occupant and contract evidence, the approved billing amount apart from the contract's own
/// rental, the stall payment calendar fed by the period register, and the stall footer. Nothing is repriced, and no source identifier is shown.
/// </summary>
public sealed class SpaceRentalDetailsDrawerTests : TestContext
{
    private readonly Mock<IObligationsApiClient> _api = new();
    private static readonly DateOnly Today = PhilippineTime.Today;

    public SpaceRentalDetailsDrawerTests()
    {
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IEcfCollectionsApiClient>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static ObligationAccountDto Monthly(ObligationKind kind = ObligationKind.KanmanggaySpaceRental) =>
        new(Guid.NewGuid(), kind, "Kanmanggay", Guid.NewGuid(), "Payor", null, null, "12", null, null, new DateOnly(Today.Year, 1, 1), null, 900m, null, 1800m, 900m, 900m,
            OccupancyArrangement.SignedContract, "KMG-12", "Elena Ramos", "Elena R. Santos", new DateOnly(Today.Year, 1, 1), 3, 9.5m, "Row A", 1000m);

    private static ObligationQuoteDto Period(ObligationAccountDto a, DateOnly month, decimal settled) =>
        new(a.Id, Guid.NewGuid(), a.Kind, a.KindLabel, a.SubjectLabel, month, 900m, settled, 900m - settled, a.PayorId, "Payor", null, true);

    private IRenderedComponent<ObligationHolderDetails> Open(ObligationAccountDto account, bool edit = true, params ObligationQuoteDto[] periods)
    {
        _api.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success(periods));
        var cut = RenderComponent<ObligationHolderDetails>(p => p.Add(x => x.Account, account).Add(x => x.Operation, "Kanmanggay").Add(x => x.CanEdit, edit)
            .Add(x => x.OnEdit, EventCallback.Factory.Create(this, () => { })).Add(x => x.OnHistory, EventCallback.Factory.Create(this, () => { })));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".dv-summary")));
        return cut;
    }

    [Fact]
    public void AMonthlySpaceOpensTheStallDrawer_WithContractEvidenceBillingApartFromTheContractRental_AndTheStallFooter()
    {
        var account = Monthly();
        var cut = Open(account, true, Period(account, new DateOnly(Today.Year, Today.Month, 1), 900m));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".mo-cal-grid")));
        Assert.Equal("Elena Ramos", cut.Find(".dv-title").TextContent);
        Assert.Contains("Space 12", cut.Find(".dv-context").TextContent);
        Assert.Contains("Active", cut.Find(".dv-context").TextContent);
        var text = cut.Find(".detail-modal").TextContent;
        foreach (var expected in new[] { "Occupant & contract", "Actual occupant", "Name on contract", "Elena R. Santos", "Signed lease contract", "KMG-12", "3 years", "Fee structure",
                     "Approved monthly rental", "₱900", "Rental per contract", "Evidence only", "₱1,000", "9.5 sq.m" })
            Assert.Contains(expected, text);
        Assert.Contains("Paid", cut.Find(".dv-summary .status-pill").TextContent);                    // this month, from the period register
        Assert.DoesNotContain(account.Id.ToString(), cut.Markup);                                     // no source identifier
        Assert.Equal(["Close", "History", "Edit Occupant & Space"], cut.FindAll(".dv-footer button").Select(b => b.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void ASpaceOnlyAccountInventsNoContract_AndAClosedOrReadOnlyOneOffersNoEdit()
    {
        var spaceOnly = Monthly() with { Arrangement = OccupancyArrangement.SpaceOnly, ContractReference = null, ContractName = null, ContractEffectiveDate = null, ContractYears = null, ContractMonthlyRate = null };
        var cut = Open(spaceOnly, edit: false);
        var text = cut.Find(".detail-modal").TextContent;
        Assert.Contains("No contract", text);
        Assert.DoesNotContain("Rental per contract", text);
        Assert.DoesNotContain("KMG-12", text);
        Assert.Equal(["Close", "History"], cut.FindAll(".dv-footer button").Select(b => b.TextContent.Trim()).ToArray());

        var closed = Open(Monthly() with { ActiveTo = new DateOnly(Today.Year, 6, 30) }, edit: true);
        Assert.Contains("Closed", closed.Find(".dv-context").TextContent);
        Assert.DoesNotContain(closed.FindAll(".dv-footer button"), b => b.TextContent.Contains("Edit"));
    }

    [Fact]
    public void AnEventLotKeepsItsEventFacts_NoMonthlyCalendar_AndNoEdit()
    {
        var lot = Monthly(ObligationKind.FiestaArawLotRental) with { Event = LotRentalEvent.Araw, EventDate = Today, ContractMonthlyRate = null, ContractName = null };
        var cut = Open(lot, edit: true);
        var text = cut.Find(".detail-modal").TextContent;
        foreach (var expected in new[] { "Lot holder & event", "Araw", "Lot no.", "Approved event amount", "One-time", "Event settlement" }) Assert.Contains(expected, text);
        Assert.Empty(cut.FindAll(".mo-cal-grid"));
        Assert.DoesNotContain(cut.FindAll(".dv-footer button"), b => b.TextContent.Contains("Edit"));
        _api.Verify(x => x.GetRegisterAsync(It.IsAny<Guid>()), Times.Never);   // an event has no monthly register to read
    }

    [Fact]
    public void AFailedPeriodReadIsStated_NotAnEmptyCalendar()
    {
        var account = Monthly();
        _api.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Failure("offline"));
        var cut = RenderComponent<ObligationHolderDetails>(p => p.Add(x => x.Account, account).Add(x => x.Operation, "Kanmanggay"));
        cut.WaitForAssertion(() => Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent));
        Assert.Empty(cut.FindAll(".mo-cal-grid"));
    }

    [Fact]
    public void ThePaymentMonthsMapTheAssessedPeriodsToPaidPartialAndUnpaid_AndSkipUnassessedOnes()
    {
        var a = Monthly();
        var rows = SpacePaymentMonths.From([Period(a, new DateOnly(2026, 1, 1), 900m), Period(a, new DateOnly(2026, 2, 1), 300m), Period(a, new DateOnly(2026, 3, 1), 0m),
            Period(a, new DateOnly(2026, 4, 1), 0m) with { AssessedAmount = 0m, OutstandingAmount = 0m }]);
        Assert.Equal([("2026-01", PaymentStatus.Paid, 900m), ("2026-02", PaymentStatus.Partial, 300m), ("2026-03", PaymentStatus.Unpaid, 0m)],
            rows.Select(r => (r.Period, r.Status, r.AmountPaid)).ToArray());
    }
}
