using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using PayorBalances = EEMOCantilanSDS.Client.Components.Pages.Payor.PayorBalances;

/// <summary>The payor's balance (2026-11-25): a late month states its surcharge and interest, months ahead are never part of what is due, and rent is paid ahead in one checkout.</summary>
public class PayorBalancesTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Guid Stall = Guid.NewGuid();

    private Mock<IPayorApiClient> Render(IReadOnlyList<PayorPayableItemDto> items, PayAheadOptionsDto options, IReadOnlyList<PayAheadMonthDto>? paid = null)
    {
        var api = new Mock<IPayorApiClient>();
        api.Setup(a => a.GetPayableItemsAsync()).ReturnsAsync(Result<IReadOnlyList<PayorPayableItemDto>>.Success(items));
        api.Setup(a => a.GetPayAheadAsync()).ReturnsAsync(Result<IReadOnlyList<PayorPayAheadDto>>.Success(
            [new(Stall, "7", FacilityCode.TCC, "Tampak Commercial Center", "TCC", options, paid ?? [])]));
        api.Setup(a => a.InitiatePaymentAsync(It.IsAny<InitiateOnlinePaymentCommand>()))
            .ReturnsAsync(Result<InitiateOnlinePaymentResultDto>.Failure("stop here", ResultStatus.Conflict));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("Maria Santos");
        auth.SetRoles("Payor");
        return api;
    }

    private static PayorPayableItemDto Month(int year, int month, decimal rent) => new(Stall, "7", FacilityCode.TCC, year, month, $"{year:0000}-{month:00}", rent);

    [Fact]
    public void ALateMonth_StatesItsSurchargeAndInterest_AndTheTotalLeavesOutAMonthAhead()
    {
        Render([
            Month(2026, 8, 1_000m) with { PreviousBalance = true, Surcharge = 250m, Interest = 75m },
            Month(2026, 9, 1_000m) with { PreviousBalance = true, PayableOnline = false, PayFirst = "2026-08" },
            Month(2026, 12, 1_000m) with { Advance = true }
        ], new([], [], "Collect the ₱2,325 owed first."));
        var cut = RenderComponent<PayorBalances>();

        cut.WaitForAssertion(() => Assert.Contains("Overdue", cut.Markup), Timeout);
        Assert.Contains("₱2,325.00", cut.Find(".pp-hero-amount").TextContent);   // August 1,325 with its charges + September 1,000; December is not owed
        Assert.Contains("Surcharge", cut.Markup);
        Assert.Contains("₱250.00", cut.Markup);
        Assert.Contains("Pay August 2026 first", cut.Markup);
        Assert.Contains("Pay what you owe first", cut.Markup);
        Assert.Empty(cut.FindAll(".pp-ahead-count"));
    }

    [Fact]
    public void NothingOwed_PaysThreeMonthsAhead_InOneCheckout_AndShowsTheMonthsAlreadyPaidAhead()
    {
        var api = Render([], new([new(2027, 1, 1_000m), new(2027, 2, 1_000m), new(2027, 3, 1_000m)], [1, 2, 3], null), [new(2026, 12, 1_000m)]);
        var cut = RenderComponent<PayorBalances>();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".pp-ahead-count").Count), Timeout);
        Assert.Contains("December 2026", cut.Find(".pp-ahead-paid").TextContent);
        cut.FindAll(".pp-ahead-count")[2].Click();
        Assert.Contains("₱3,000.00", cut.Find(".pp-ahead-pay").TextContent);
        cut.Find(".pp-ahead-pay").Click();

        api.Verify(a => a.InitiatePaymentAsync(It.Is<InitiateOnlinePaymentCommand>(c =>
            c.StallId == Stall && c.Year == 2027 && c.Month == 1 && c.Months == 3 && c.Kind == PayorPayableKind.Monthly)), Times.Once);
    }
}
