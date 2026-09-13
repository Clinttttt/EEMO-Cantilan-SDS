using Bunit;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using PayBillModal = EEMOCantilanSDS.Client.Components.Pages.Shared.Actions.PayBillModal;

/// <summary>
/// Settling a daily-collected market account in ONE transaction: the office's "he will pay it all today".
///
/// <para>
/// The monthly facilities have always offered "All outstanding". The market could only be settled a month or a day at a
/// time, so twelve owed months meant twelve separate acts and twelve chances to mistype a receipt. It is the same handler
/// per month either way — <c>SettleNpmMonthCommandHandler</c> — so every rule it enforces still applies to each month: the
/// month ceiling that stops a month collecting more than its rent, excused days and market closures, and its refusal of a
/// month the office never priced by the day. What changes is only that the office states them once.
/// </para>
///
/// <para>
/// One receipt covers them all, which the OR registry already permitted: the daily allowance is per STALL, rejecting an OR
/// only where it appears on a different stall, and it was never scoped to a month.
/// </para>
/// </summary>
public class PayBillModalSettleEverythingTests : TestContext
{
    private static readonly Guid StallId = Guid.NewGuid();
    private static readonly Guid ContractId = Guid.NewGuid();

    private readonly List<SettleNpmMonthCommand> _settled = new();
    private Mock<IDailyCollectionApiClient> _daily = null!;

    /// <summary>
    /// Twelve owed months of a ₱900 space — ₱10,800, the office's own Whole Year Rental figure.
    /// </summary>
    /// <remarks>
    /// Deliberately spanning TWO calendar years, July 2023 to June 2024. Within a single year an "oldest first" ordering
    /// cannot be told apart from one that sorts by month alone, so a wrong sort would pass unnoticed.
    /// </remarks>
    private static readonly (int Year, int Month)[] OwedMonths =
        Enumerable.Range(0, 12).Select(i => (Year: 2023 + (6 + i) / 12, Month: (6 + i) % 12 + 1)).ToArray();

    private static List<PaymentHistoryDto> TwelveOwedMonths() =>
        OwedMonths
            .Select(m => new PaymentHistoryDto(
                Period: $"{m.Year:D4}-{m.Month:D2}",
                Status: PaymentStatus.Unpaid,
                TotalBill: 900m,
                AmountPaid: 0m,
                BalanceDue: 900m,
                ORNumber: null,
                PaidAt: null))
            .ToList();

    private IRenderedComponent<PayBillModal> RenderModal(
        List<PaymentHistoryDto>? months = null,
        string? failOnPeriod = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var payments = new Mock<IPaymentsApiClient>();
        payments.Setup(p => p.GetOutstandingMonthsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success(months ?? TwelveOwedMonths()));

        _daily = new Mock<IDailyCollectionApiClient>();
        _daily.Setup(d => d.GetSettleableNpmDaysAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Result<IReadOnlyList<SettleableNpmDayDto>>.Success(Array.Empty<SettleableNpmDayDto>()));
        _daily.Setup(d => d.SettleNpmMonthAsync(It.IsAny<SettleNpmMonthCommand>()))
            .Returns((SettleNpmMonthCommand c) =>
            {
                _settled.Add(c);
                return Task.FromResult(failOnPeriod == $"{c.Year:D4}-{c.Month:D2}"
                    ? Result<bool>.Failure("OR number already exists.")
                    : Result<bool>.Success(true));
            });

        Services.AddSingleton(payments.Object);
        Services.AddSingleton(_daily.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        return RenderComponent<PayBillModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.StallId, StallId)
            .Add(c => c.Facility, FacilityCode.NPM)
            .Add(c => c.StallNo, "6")
            .Add(c => c.Occupant, "Teofila Reyes")
            .Add(c => c.ContractId, ContractId));
    }

    private static void ChooseAllOutstanding(IRenderedComponent<PayBillModal> cut) =>
        cut.FindAll(".pb-seg-btn").Single(b => b.TextContent.Contains("All outstanding")).Click();

    [Fact]
    public void TheMarketIsOfferedAllOutstanding_AlongsideItsMonthAndDayChoices()
    {
        var cut = RenderModal();

        var choices = cut.WaitForElements(".pb-seg-btn").Select(b => b.TextContent.Trim()).ToList();

        Assert.Contains("Whole month", choices);
        Assert.Contains("Specific days", choices);
        Assert.Contains("All outstanding", choices);
    }

    [Fact]
    public void ChoosingIt_StatesTheWholeBalanceAsTheAmountToRecord()
    {
        var cut = RenderModal();
        cut.WaitForElements(".pb-seg-btn");

        ChooseAllOutstanding(cut);

        // Twelve months of ₱900. The office's own Whole Year Rental figure, settled as one act.
        Assert.Contains("10,800.00", cut.Markup);
        Assert.Contains("all outstanding months", cut.Markup);
    }

    [Fact]
    public void Recording_SettlesEveryOwedMonthOldestFirst_UnderOneReceipt()
    {
        var cut = RenderModal();
        cut.WaitForElements(".pb-seg-btn");
        ChooseAllOutstanding(cut);

        cut.Find("#pb-or").Input("1234567");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Record ")).Click();

        cut.WaitForAssertion(() => Assert.Equal(12, _settled.Count));

        // Oldest first, across the year boundary: if a run ever stops part way, the account is settled from the back, which
        // is how arrears are read. Asserted as (year, month) pairs so a sort that ignores the year cannot pass.
        Assert.Equal(OwedMonths, _settled.Select(c => (c.Year, c.Month)).ToArray());

        // One receipt across them all, and every month scoped to the same occupancy.
        Assert.All(_settled, c => Assert.Equal("1234567", c.ORNumber));
        Assert.All(_settled, c => Assert.Equal(StallId, c.StallId));
        Assert.All(_settled, c => Assert.Equal(ContractId, c.ContractId));
    }

    [Fact]
    public void WhenARunStopsPartWay_ItSaysHowManyMonthsWentIn()
    {
        // Each month is its own transaction, so the months already recorded stand. Saying only "could not record the
        // payment" would leave the office believing nothing was taken when three months had been.
        // The run is July 2023 first, so October 2023 is the fourth month: three go in, then it stops.
        var cut = RenderModal(failOnPeriod: "2023-10");
        cut.WaitForElements(".pb-seg-btn");
        ChooseAllOutstanding(cut);

        cut.Find("#pb-or").Input("1234567");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Record ")).Click();

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            Assert.Contains("3 months were recorded", markup);
            Assert.Contains("October 2023", markup);
        });

        // It stopped at the failure rather than pressing on into November and beyond.
        Assert.Equal(4, _settled.Count);
    }

    [Fact]
    public void WithNothingOutstanding_ThereIsNothingToRecord()
    {
        var cut = RenderModal(months: new List<PaymentHistoryDto>());

        // The modal states an empty account instead of offering a transaction of zero.
        cut.WaitForAssertion(() => Assert.Empty(_settled));
        Assert.DoesNotContain("Record ₱", cut.Markup);
    }

    [Fact]
    public void TheSettleControlKeepsItsThreeChoicesOnOneRow()
    {
        // Adding "All outstanding" to a control hard-coded at two columns wrapped it onto a second row and broke the control
        // in half. The count is stated by the markup so the grid follows the choices rather than the other way round.
        var cut = RenderModal();
        cut.WaitForElements(".pb-seg-btn");

        Assert.Equal(3, cut.FindAll(".pb-seg-btn").Count);
        Assert.Contains("--pb-seg-count: 3", cut.Find(".pb-seg").GetAttribute("style"));
    }
}
