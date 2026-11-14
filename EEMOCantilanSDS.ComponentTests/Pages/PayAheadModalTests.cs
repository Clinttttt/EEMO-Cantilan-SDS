using Bunit;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayAhead;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using PayBillModal = EEMOCantilanSDS.Client.Components.Pages.Shared.Actions.PayBillModal;

/// <summary>The office takes rent ahead from the same dialog, on months and a total the server stated, with one receipt.</summary>
public sealed class PayAheadModalTests : TestContext
{
    private static readonly Guid Stall = Guid.NewGuid();
    private readonly Mock<IPaymentsApiClient> _payments = new();

    private static PayAheadOptionsDto Options(params int[] counts) => new(
        Enumerable.Range(0, counts.Max()).Select(i => new PayAheadMonthDto(2026 + (10 + i) / 12, (10 + i) % 12 + 1, 1_000m)).ToList(), counts, null);

    private IRenderedComponent<PayBillModal> Open(Result<PayAheadOptionsDto> options, Guid? contract = null, bool startAhead = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _payments.Setup(x => x.GetOutstandingMonthsAsync(Stall, contract, null, null)).ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([]));
        _payments.Setup(x => x.GetPayAheadOptionsAsync(Stall)).ReturnsAsync(options);
        Services.AddSingleton(_payments.Object);
        Services.AddSingleton(Mock.Of<IEcfCollectionsApiClient>());
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>()); Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>()); Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());
        Services.AddSingleton(new EEMOCantilanSDS.Client.Services.BrandingState(Mock.Of<IMunicipalitiesApiClient>()));
        var cut = RenderComponent<PayBillModal>(p => p.Add(x => x.Show, true).Add(x => x.StallId, Stall).Add(x => x.ContractId, contract).Add(x => x.Facility, FacilityCode.TCC).Add(x => x.StartAhead, startAhead));
        cut.WaitForAssertion(() => Assert.Contains(startAhead ? "pb-ahead-or" : "No outstanding balance", cut.Markup));
        return cut;
    }

    private static void ChooseYear(IRenderedComponent<PayBillModal> cut)
    {
        cut.FindAll("button.pb-seg-btn").Single(b => b.TextContent == "Pay ahead").Click();
        cut.Find("button.fh-dd-trigger").Click();
        cut.FindAll(".fh-dd-item").Single(i => i.TextContent.Contains("one year")).Click();
    }

    [Fact]
    public void AFullyPaidAccount_CanStillPayAYear_OnOneReceipt()
    {
        RecordPayAheadCommand? sent = null;
        _payments.Setup(x => x.RecordPayAheadAsync(It.IsAny<RecordPayAheadCommand>()))
            .Callback((RecordPayAheadCommand c) => sent = c).ReturnsAsync(Result<bool>.Success(true));
        var cut = Open(Result<PayAheadOptionsDto>.Success(Options(1, 2, 12)));

        ChooseYear(cut);

        Assert.Equal(12, cut.FindAll(".pb-table tbody tr").Count);
        Assert.Contains("₱12,000.00", cut.Find("button.btn-primary").TextContent);
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));   // no receipt yet

        cut.Find("#pb-ahead-or").Input("  1234567 ");
        cut.Find("button.btn-primary").Click();

        cut.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Equal(new RecordPayAheadCommand(Stall, 2026, 11, 12, 12_000m, "1234567"), sent);
    }

    [Fact]
    public void ACallerFromAMonthToCome_OpensOnPayAhead_AndARefusalStaysOnScreen_OutsideTheScrollingBody()
    {
        _payments.Setup(x => x.RecordPayAheadAsync(It.IsAny<RecordPayAheadCommand>())).ReturnsAsync(Result<bool>.Failure("OR number already exists.", ResultStatus.Conflict));
        var cut = Open(Result<PayAheadOptionsDto>.Success(Options(1)), startAhead: true);

        cut.Find("#pb-ahead-or").Input("1234567");
        cut.Find("button.btn-primary").Click();

        cut.WaitForAssertion(() => Assert.Contains("OR number already exists.", cut.Find(".pb-shell > .pb-error[role='alert']").TextContent));
        Assert.Empty(cut.FindAll(".pb-body .pb-error"));
    }

    [Fact]
    public void ARefusal_IsStated_AndTheFormStaysOpen()
    {
        _payments.Setup(x => x.RecordPayAheadAsync(It.IsAny<RecordPayAheadCommand>())).ReturnsAsync(Result<bool>.Failure("OR number already exists.", ResultStatus.Conflict));
        var cut = Open(Result<PayAheadOptionsDto>.Success(Options(1, 2, 12)));
        ChooseYear(cut);

        cut.Find("#pb-ahead-or").Input("1234567");
        cut.Find("button.btn-primary").Click();

        cut.WaitForAssertion(() => Assert.Equal("OR number already exists.", cut.Find(".pb-error").TextContent.Trim()));
        Assert.Equal("1234567", cut.Find("#pb-ahead-or").GetAttribute("value"));
    }

    [Fact]
    public void AFormerOccupancy_IsNotOfferedPayAhead()
    {
        var cut = Open(Result<PayAheadOptionsDto>.Success(Options(1, 2, 12)), contract: Guid.NewGuid());

        Assert.Empty(cut.FindAll("button.pb-seg-btn"));
        _payments.Verify(x => x.GetPayAheadOptionsAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void NothingOfferedByTheServer_ShowsNoPayAhead()
    {
        var cut = Open(Result<PayAheadOptionsDto>.Success(new([], [], "Pay March 2026 first.")));

        Assert.Empty(cut.FindAll("button.pb-seg-btn"));
    }

    [Fact]
    public void AFailedRead_SaysSo()
    {
        var cut = Open(Result<PayAheadOptionsDto>.Failure("Unavailable"));

        Assert.Contains("Pay ahead could not be loaded", cut.Markup);
    }
}
