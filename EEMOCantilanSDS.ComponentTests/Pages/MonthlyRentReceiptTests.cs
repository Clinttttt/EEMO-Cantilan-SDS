using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Shared.Actions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class MonthlyRentReceiptTests : TestContext
{
    [Theory]
    [InlineData(FacilityCode.NCC, true)]
    [InlineData(FacilityCode.TCC, true)]
    [InlineData(FacilityCode.BBQ, true)]
    [InlineData(FacilityCode.ICE, true)]
    [InlineData(FacilityCode.NCC, false)]
    public void Canonical_Head_final_remainder_obeys_activation_and_keeps_frozen_retry_without_legacy_write(FacilityCode facility, bool enabled)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var stall = Guid.NewGuid(); var contract = Guid.NewGuid(); var posts = new List<MobileRentPostRequest>();
        var payments = new Mock<IPaymentsApiClient>(); var rent = new Mock<IEcfCollectionsApiClient>();
        payments.Setup(x => x.GetOutstandingMonthsAsync(stall, contract, null, null)).ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([
            new("2026-10", PaymentStatus.Partial, 900, 875, 25, null, null, SettlementAuthority: SettlementAuthority.Canonical)]));
        rent.Setup(x => x.GetRentObligationAsync(stall, 2026, 10)).ReturnsAsync(Result<RentObligationQuoteDto>.Success(
            new(Guid.NewGuid(), Guid.NewGuid(), CollectionSourceKind.PaymentRecord, stall, "1", facility.ToString(), 2026, 10,
                contract, null, "Verified occupant", 900, 875, 25, SettlementAuthority.Canonical, 7, Guid.NewGuid(), Guid.NewGuid(), "Rent", RevenueInstrumentType.OfficialReceipt, 900, true, true, false, enabled)));
        rent.Setup(x => x.PostRentReceiptAsync(It.IsAny<MobileRentPostRequest>())).Returns((MobileRentPostRequest request) =>
        { posts.Add(request); throw new HttpRequestException("Synthetic lost acknowledgement"); });
        Services.AddSingleton(payments.Object); Services.AddSingleton(rent.Object);
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>()); Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>()); Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());
        Services.AddSingleton(new EEMOCantilanSDS.Client.Services.BrandingState(Mock.Of<IMunicipalitiesApiClient>()));
        var cut = RenderComponent<PayBillModal>(p => p.Add(x => x.Show, true).Add(x => x.StallId, stall).Add(x => x.ContractId, contract).Add(x => x.Facility, facility));
        if (!enabled)
        {
            cut.WaitForAssertion(() => Assert.Contains("receipts are paused", cut.Markup));
            Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled")); Assert.Empty(posts); return;
        }
        cut.WaitForAssertion(() => Assert.Contains("A canonical SRC", cut.Markup));
        Assert.Empty(cut.FindAll("#pb-or"));
        if (facility == FacilityCode.NCC) cut.Find("#pb-rent-received").Change("10.00");
        cut.Find("button.btn-primary").Click(); cut.WaitForAssertion(() => Assert.Single(posts));
        cut.WaitForAssertion(() => Assert.Contains("outcome unknown", cut.Markup));
        cut.Find("button.btn-primary").Click(); cut.WaitForAssertion(() => Assert.Equal(2, posts.Count));
        Assert.Equal(posts[0], posts[1]); Assert.Equal(facility == FacilityCode.NCC ? 10 : 25, posts[0].ReceivedAmount); Assert.Equal(7, posts[0].SourceVersion);
        Assert.True(cut.Find("#pb-rent-received").HasAttribute("disabled"));
        Assert.NotEqual(Guid.Empty, posts[0].ClientOperationId);
        Assert.Equal(contract, posts[0].ExpectedContractId);
        payments.Verify(x => x.RecordPaymentAsync(It.IsAny<EEMOCantilanSDS.Application.Command.Payments.RecordPayment.RecordPaymentCommand>()), Times.Never);
    }
}
