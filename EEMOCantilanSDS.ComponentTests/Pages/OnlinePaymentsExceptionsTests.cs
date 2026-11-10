using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>Online Payments → Needs attention: both the Head and Admins see it, and only the Head is offered the decision.</summary>
public sealed class OnlinePaymentsExceptionsTests : TestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly Mock<IOnlinePaymentsApiClient> _online = new();

    private IRenderedComponent<OnlinePayments> Open(string role, Result<IReadOnlyList<OnlinePaymentExceptionDto>> exceptions, PaymentSettingsDto? payment = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Staff").SetRoles(role);
        var settings = new Mock<ISettingsApiClient>();
        settings.Setup(a => a.GetPaymentSettingsAsync()).ReturnsAsync(payment is null
            ? Result<PaymentSettingsDto>.Failure("not needed here", 500)
            : Result<PaymentSettingsDto>.Success(payment));
        Services.AddSingleton(settings.Object);
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        // Stated, because BrandingState answers as the default office until it loads, and the default office always has a way back from setup.
        var branding = new BrandingState(Mock.Of<IMunicipalitiesApiClient>());
        branding.Apply(new MunicipalityBrandingDto(
            Code: "OTHER", TenantCode: "other", Name: "Other", Province: "Province",
            OfficeName: "Office", SealPath: null, Status: "Active", IsActive: true,
            OfficeAcronym: null, Address: null, ReportSignatories: null, IsDefault: false));
        Services.AddSingleton(branding);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        _online.Setup(a => a.GetAwaitingOrAsync()).ReturnsAsync(Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>.Success([]));
        _online.Setup(a => a.GetDashboardAsync()).ReturnsAsync(Result<OnlinePaymentDashboardDto>.Failure("not needed here", 500));
        _online.Setup(a => a.GetExceptionsAsync()).ReturnsAsync(exceptions);
        Services.AddSingleton(_online.Object);
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success([]));
        Services.AddSingleton(facilities.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));
        return RenderComponent<OnlinePayments>();
    }

    private static Result<IReadOnlyList<OnlinePaymentExceptionDto>> Rows(params OnlinePaymentExceptionDto[] rows) =>
        Result<IReadOnlyList<OnlinePaymentExceptionDto>>.Success(rows);

    private static OnlinePaymentExceptionDto Row(OnlinePaymentExceptionKind kind, string reference = "EEMO-OP-HELD", decimal? captured = null,
        OnlinePaymentDisposition? disposition = null, string? dispositionReference = null) =>
        new(Guid.NewGuid(), reference, kind, "Town Center", "7", "Angelica Reyes", "2026-06", 100m, captured, "qrph",
            new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc), null, "The gateway captured 90.00 but the checkout asked for 100.00.",
            disposition, dispositionReference, null, disposition is null ? null : "head.officer",
            disposition is null ? null : new DateTime(2026, 6, 2, 3, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void AnAdminSeesTheHeldMoney_ButIsNotOfferedTheDecision()
    {
        var cut = Open("Admin", Rows(Row(OnlinePaymentExceptionKind.NeedsDecision, captured: 90m)));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Needs attention", cut.Markup);
            Assert.Contains("Received, not applied", cut.Markup);
            Assert.Contains("90.00 received", cut.Markup);
            Assert.Contains("100.00 asked", cut.Markup);
            Assert.Contains("The gateway captured 90.00", cut.Markup);
            Assert.Contains("Waiting for the Head", cut.Markup);
            Assert.DoesNotContain("Record decision", cut.Markup);
        }, Wait);
    }

    [Fact]
    public void AHeadWhoseGatewayIsDisconnected_CanStillReachHeldMoney()
    {
        var cut = Open("SuperAdmin", Rows(Row(OnlinePaymentExceptionKind.NeedsDecision)), new PaymentSettingsDto(false, null, false));

        cut.WaitForAssertion(() => Assert.Contains("Back to payments", cut.Markup), Wait);
        cut.FindAll("button").First(b => b.TextContent.Contains("Back to payments")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Record decision", cut.Markup), Wait);
    }

    [Fact]
    public void AnOfficeWithNoGatewayAndNothingHeld_StaysOnTheSetupScreen()
    {
        var cut = Open("SuperAdmin", Rows(), new PaymentSettingsDto(false, null, false));

        cut.WaitForAssertion(() => Assert.Contains("Test connection", cut.Markup), Wait);
        Assert.DoesNotContain("Back to payments", cut.Markup);
    }

    [Fact]
    public void TheHeadRecordsADecision_WithTheTrimmedReference_AndTheListIsReRead()
    {
        var row = Row(OnlinePaymentExceptionKind.NeedsDecision);
        var cut = Open("SuperAdmin", Rows(row));
        _online.Setup(a => a.ResolveExceptionAsync(row.TransactionId, OnlinePaymentDisposition.Applied, "OR-2026-77", "Issued at the window"))
            .ReturnsAsync(Result<bool>.Success(true));

        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent == "Record decision"), Wait);
        cut.FindAll("button").Single(b => b.TextContent == "Record decision").Click();

        // The save stays off until a reference is given, so a decision can't be recorded with nothing to trace it to.
        Assert.True(cut.FindAll("button").Single(b => b.TextContent == "Save decision").HasAttribute("disabled"));

        cut.FindAll("input[name=op-decide-kind]")[1].Change(true);
        Assert.Contains("OR number", cut.Find("label[for=op-decide-reference]").TextContent);
        cut.Find("#op-decide-reference").Input("  OR-2026-77 ");
        cut.Find("#op-decide-note").Input("Issued at the window");
        cut.FindAll("button").Single(b => b.TextContent == "Save decision").Click();

        cut.WaitForAssertion(() =>
        {
            _online.Verify(a => a.ResolveExceptionAsync(row.TransactionId, OnlinePaymentDisposition.Applied, "OR-2026-77", "Issued at the window"), Times.Once);
            Assert.Contains("Decision recorded", cut.Markup);
            Assert.Empty(cut.FindAll("[role=dialog]"));
        }, Wait);
        _online.Verify(a => a.GetExceptionsAsync(), Times.Exactly(2));
    }

    [Fact]
    public void ARefusedDecision_StaysOpenAndSaysWhy()
    {
        var row = Row(OnlinePaymentExceptionKind.NeedsDecision);
        var cut = Open("SuperAdmin", Rows(row));
        _online.Setup(a => a.ResolveExceptionAsync(row.TransactionId, OnlinePaymentDisposition.Refunded, "ref_1", null))
            .ReturnsAsync(Result<bool>.Failure("This payment is not awaiting a decision. Refresh the list.", ResultStatus.Conflict));

        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent == "Record decision"), Wait);
        cut.FindAll("button").Single(b => b.TextContent == "Record decision").Click();
        cut.Find("#op-decide-reference").Input("ref_1");
        cut.FindAll("button").Single(b => b.TextContent == "Save decision").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("not awaiting a decision", cut.Find("[role=dialog] [role=alert]").TextContent);
            Assert.NotEmpty(cut.FindAll("[role=dialog]"));
        }, Wait);
    }

    [Fact]
    public void CheckingWithTheGateway_SaysWhatItFound_AndReReadsThePage()
    {
        var row = Row(OnlinePaymentExceptionKind.Unconfirmed, "EEMO-OP-STALE");
        var cut = Open("Admin", Rows(row));
        _online.Setup(a => a.VerifyAsync("EEMO-OP-STALE"))
            .ReturnsAsync(Result<ConfirmOnlinePaymentResultDto>.Success(new ConfirmOnlinePaymentResultDto("Paid", true)));

        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Check with gateway"), Wait);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Check with gateway").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("the gateway has this payment and it has been recorded", cut.Markup);
            _online.Verify(a => a.GetExceptionsAsync(), Times.Exactly(2));
            _online.Verify(a => a.GetAwaitingOrAsync(), Times.Exactly(2));
        }, Wait);
    }

    [Fact]
    public void AFailedLoad_SaysSo_RatherThanLookingLikeNothingIsWrong()
    {
        var cut = Open("Admin", Result<IReadOnlyList<OnlinePaymentExceptionDto>>.Failure("down", 500));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("This does not mean there are none", cut.Find("[role=alert]").TextContent);
            Assert.DoesNotContain("Needs attention", cut.Markup);
        }, Wait);
    }

    [Fact]
    public void NothingOpenAndNothingDecided_ShowsNoSection()
    {
        var cut = Open("Admin", Rows());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("awaiting Official Receipt", cut.Markup);
            Assert.DoesNotContain("Needs attention", cut.Markup);
        }, Wait);
    }

    [Fact]
    public void DecidedRows_StayFoldedUntilAsked_AndShowWhoDecided()
    {
        var cut = Open("Admin", Rows(Row(OnlinePaymentExceptionKind.Resolved, disposition: OnlinePaymentDisposition.Refunded, dispositionReference: "ref_77")));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Nothing open.", cut.Markup);
            Assert.DoesNotContain("ref_77", cut.Markup);
        }, Wait);

        cut.FindAll("button").Single(b => b.TextContent.Contains("Show decided (1)")).Click();

        Assert.Contains("ref_77", cut.Markup);
        Assert.Contains("head.officer", cut.Markup);
        Assert.Contains("Refunded", cut.Markup);
    }
}
