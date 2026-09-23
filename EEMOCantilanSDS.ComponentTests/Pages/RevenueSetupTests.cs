using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Securities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.HttpClients.ApiClients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class RevenueSetupTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Page_IsRestrictedToSuperAdmin()
    {
        var authorize = Assert.Single(typeof(RevenueSetup).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        var route = Assert.Single(typeof(RevenueSetup).GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>());

        Assert.Equal("SuperAdmin", authorize.Roles);
        Assert.Equal("/settings/revenue", route.Template);
    }

    [Fact]
    public void Register_ShowsActiveRetiredNullInstrumentAndMissingPolicyStates()
    {
        var active = Classification("ECF", "Environmental Fee", true, true,
            Policy("ECF", "Environmental Fee", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt));
        var noHistory = Classification("MISC", "", true, false, null);
        var futureOnly = Classification("FUTURE", "", true, true, null);
        var retired = Classification("ARREARS", "Arrears", false, true,
            Policy("ARREARS", "Arrears", PhilippineTime.Today, null));
        var (cut, _) = RenderPage([active, noHistory, futureOnly, retired]);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Environmental Fee", cut.Markup);
            Assert.Contains("Official Receipt", cut.Markup);
            Assert.Contains("Retired", cut.Markup);
            Assert.Contains("Not configured", cut.Markup);
            Assert.Contains("Active · No policy configured", cut.Markup);
            Assert.Contains("Active · No policy effective yet", cut.Markup);
            Assert.Contains("Internal reference", cut.Markup);
            Assert.DoesNotContain("Accounting code", cut.Find(".rev-source-code").TextContent);
            Assert.Equal(4, cut.FindAll(".rev-table tbody tr").Count);
        }, RenderTimeout);
    }

    [Fact]
    public void AsOfDate_ReloadsRegisterWithRequestedBusinessDate()
    {
        var (cut, api) = RenderPage([]);
        cut.WaitForAssertion(() => Assert.NotEmpty(api.Invocations), RenderTimeout);
        var date = new DateOnly(2026, 9, 30);

        cut.Find("input[aria-label='View revenue policies as of date']").Change("2026-09-30");

        cut.WaitForAssertion(() => api.Verify(client => client.GetClassificationsAsync(date), Times.Once), RenderTimeout);
        Assert.Contains("value=\"2026-09-30\"", cut.Markup);
    }

    [Fact]
    public async Task AsOfLoads_OutOfOrderSuccessKeepsLatestDateAndData()
    {
        var firstDate = PhilippineTime.Today.AddDays(-1);
        var secondDate = PhilippineTime.Today.AddDays(1);
        var firstResponse = new TaskCompletionSource<Result<IReadOnlyList<RevenueClassificationDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<Result<IReadOnlyList<RevenueClassificationDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(PhilippineTime.Today))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success([]));
        api.Setup(client => client.GetClassificationsAsync(firstDate)).Returns(() => firstResponse.Task);
        api.Setup(client => client.GetClassificationsAsync(secondDate)).Returns(() => secondResponse.Task);
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("No revenue sources configured yet.", cut.Markup), RenderTimeout);

        var dateInput = cut.Find("input[aria-label='View revenue policies as of date']");
        var firstLoad = dateInput.ChangeAsync(new ChangeEventArgs { Value = DateText(firstDate) });
        var secondLoad = dateInput.ChangeAsync(new ChangeEventArgs { Value = DateText(secondDate) });
        api.Verify(client => client.GetClassificationsAsync(firstDate), Times.Once);
        api.Verify(client => client.GetClassificationsAsync(secondDate), Times.Once);

        var latest = Classification("LATEST", "Latest date source", true, true,
            Policy("LATEST", "Latest date source", secondDate, RevenueInstrumentType.CashTicket));
        secondResponse.SetResult(Result<IReadOnlyList<RevenueClassificationDto>>.Success([latest]));
        await secondLoad;
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Latest date source", cut.Find(".rev-table tbody").TextContent);
            Assert.Contains($"value=\"{DateText(secondDate)}\"", cut.Markup);
            Assert.DoesNotContain("aria-busy=\"true\"", cut.Markup);
        }, RenderTimeout);

        var stale = Classification("STALE", "Stale date source", true, true,
            Policy("STALE", "Stale date source", firstDate, RevenueInstrumentType.OfficialReceipt));
        firstResponse.SetResult(Result<IReadOnlyList<RevenueClassificationDto>>.Success([stale]));
        await firstLoad;
        Assert.Contains("Latest date source", cut.Find(".rev-table tbody").TextContent);
        Assert.DoesNotContain("Stale date source", cut.Markup);
        Assert.Contains($"value=\"{DateText(secondDate)}\"", cut.Markup);
    }

    [Fact]
    public async Task StaleAsOfSuccess_DoesNotClearLoadingWhileLatestRequestIsPending()
    {
        var firstDate = PhilippineTime.Today.AddDays(-1);
        var secondDate = PhilippineTime.Today.AddDays(1);
        var firstResponse = new TaskCompletionSource<Result<IReadOnlyList<RevenueClassificationDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<Result<IReadOnlyList<RevenueClassificationDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(PhilippineTime.Today))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success([]));
        api.Setup(client => client.GetClassificationsAsync(firstDate)).Returns(() => firstResponse.Task);
        api.Setup(client => client.GetClassificationsAsync(secondDate)).Returns(() => secondResponse.Task);
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("No revenue sources configured yet.", cut.Markup), RenderTimeout);

        var dateInput = cut.Find("input[aria-label='View revenue policies as of date']");
        var firstLoad = dateInput.ChangeAsync(new ChangeEventArgs { Value = DateText(firstDate) });
        var secondLoad = dateInput.ChangeAsync(new ChangeEventArgs { Value = DateText(secondDate) });
        cut.WaitForAssertion(() => Assert.Contains("aria-busy=\"true\"", cut.Markup), RenderTimeout);

        firstResponse.SetResult(Result<IReadOnlyList<RevenueClassificationDto>>.Success([
            Classification("STALE", "Stale date source", true, true,
                Policy("STALE", "Stale date source", firstDate, RevenueInstrumentType.OfficialReceipt))]));
        await firstLoad;
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("aria-busy=\"true\"", cut.Markup);
            Assert.DoesNotContain("Stale date source", cut.Markup);
        }, RenderTimeout);

        var latest = Classification("LATEST", "Latest date source", true, true,
            Policy("LATEST", "Latest date source", secondDate, RevenueInstrumentType.CashTicket));
        secondResponse.SetResult(Result<IReadOnlyList<RevenueClassificationDto>>.Success([latest]));
        await secondLoad;
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Latest date source", cut.Find(".rev-table tbody").TextContent);
            Assert.DoesNotContain("aria-busy=\"true\"", cut.Markup);
        }, RenderTimeout);
    }

    [Fact]
    public async Task StaleAsOfFailure_DoesNotReplaceLatestSuccessfulRegister()
    {
        var firstDate = PhilippineTime.Today.AddDays(-1);
        var secondDate = PhilippineTime.Today.AddDays(1);
        var firstResponse = new TaskCompletionSource<Result<IReadOnlyList<RevenueClassificationDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<Result<IReadOnlyList<RevenueClassificationDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(PhilippineTime.Today))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success([]));
        api.Setup(client => client.GetClassificationsAsync(firstDate)).Returns(() => firstResponse.Task);
        api.Setup(client => client.GetClassificationsAsync(secondDate)).Returns(() => secondResponse.Task);
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("No revenue sources configured yet.", cut.Markup), RenderTimeout);

        var dateInput = cut.Find("input[aria-label='View revenue policies as of date']");
        var firstLoad = dateInput.ChangeAsync(new ChangeEventArgs { Value = DateText(firstDate) });
        var secondLoad = dateInput.ChangeAsync(new ChangeEventArgs { Value = DateText(secondDate) });
        var latest = Classification("LATEST", "Latest date source", true, true,
            Policy("LATEST", "Latest date source", secondDate, RevenueInstrumentType.CashTicket));
        secondResponse.SetResult(Result<IReadOnlyList<RevenueClassificationDto>>.Success([latest]));
        await secondLoad;
        cut.WaitForAssertion(() => Assert.Contains("Latest date source", cut.Find(".rev-table tbody").TextContent), RenderTimeout);

        firstResponse.SetResult(Result<IReadOnlyList<RevenueClassificationDto>>.Failure("Stale request failed.", 500));
        await firstLoad;
        Assert.Contains("Latest date source", cut.Find(".rev-table tbody").TextContent);
        Assert.DoesNotContain("Stale request failed.", cut.Markup);
        Assert.DoesNotContain("Couldn't load revenue setup", cut.Markup);
    }

    [Fact]
    public void Create_AllowsUnconfiguredInstrument_AndRefreshesRegister()
    {
        var items = new List<RevenueClassificationDto>();
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .Returns(() => Task.FromResult(Result<IReadOnlyList<RevenueClassificationDto>>.Success(items.ToArray())));
        var id = Guid.NewGuid();
        api.Setup(client => client.CreateClassificationAsync(It.IsAny<CreateRevenueClassificationCommand>()))
            .Callback<CreateRevenueClassificationCommand>(command =>
            {
                Assert.Equal("TEMP_SPACE", command.SemanticCode);
                Assert.Equal("Temporary Space Rental", command.DisplayName);
                Assert.Equal(PhilippineTime.Today.AddDays(1), command.EffectiveDate);
                Assert.Null(command.PermittedInstrumentType);
                items.Add(Classification("TEMP_SPACE", "Temporary Space Rental", true, true,
                    Policy("TEMP_SPACE", "Temporary Space Rental", command.EffectiveDate, null, id)));
            })
            .ReturnsAsync(Result<RevenueClassificationDto>.Success(
                Classification("TEMP_SPACE", "Temporary Space Rental", true, true,
                    Policy("TEMP_SPACE", "Temporary Space Rental", PhilippineTime.Today.AddDays(1), null, id), id)));
        api.Setup(client => client.GetPolicyHistoryAsync(id))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationPolicyDto>>.Success([]));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");

        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("No revenue sources configured yet.", cut.Markup), RenderTimeout);
        ClickButton(cut, "Add revenue source");
        cut.Find(".rev-form input").Change("Temporary Space Rental");
        cut.Find(".rev-form input[type='date']").Change(DateText(PhilippineTime.Today.AddDays(1)));
        cut.Find(".rev-form input[pattern]").Change("TEMP_SPACE");
        cut.Find(".rev-form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Revenue source created.", cut.Markup);
            Assert.Contains("Temporary Space Rental", cut.Markup);
        }, RenderTimeout);
        api.Verify(client => client.CreateClassificationAsync(It.IsAny<CreateRevenueClassificationCommand>()), Times.Once);
        api.Verify(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()), Times.Exactly(2));
    }

    [Fact]
    public void Create_RejectsPastDateAndKeepsTheFormRecoverable()
    {
        var (cut, api) = RenderPage([]);
        cut.WaitForAssertion(() => Assert.Contains("No revenue sources configured yet.", cut.Markup), RenderTimeout);
        ClickButton(cut, "Add revenue source");
        cut.Find(".rev-form input").Change("Past Source");
        cut.Find(".rev-form input[type='date']").Change(DateText(PhilippineTime.Today.AddDays(-1)));
        cut.Find(".rev-form input[pattern]").Change("PAST_SOURCE");
        cut.Find(".rev-form").Submit();

        Assert.Contains("A revenue policy cannot take effect in the past.", cut.Markup);
        Assert.Single(cut.FindAll(".rev-modal"));
        api.Verify(client => client.CreateClassificationAsync(It.IsAny<CreateRevenueClassificationCommand>()), Times.Never);
    }

    [Fact]
    public async Task CreateSavingState_DisablesSubmitAndPreventsDuplicateRequests()
    {
        var id = Guid.NewGuid();
        var created = Classification("NEW_SOURCE", "New Source", true, true,
            Policy("NEW_SOURCE", "New Source", PhilippineTime.Today, null), id);
        var completion = new TaskCompletionSource<Result<RevenueClassificationDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success([]));
        api.Setup(client => client.CreateClassificationAsync(It.IsAny<CreateRevenueClassificationCommand>()))
            .Returns(() => completion.Task);
        api.Setup(client => client.GetPolicyHistoryAsync(id))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationPolicyDto>>.Success([]));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("No revenue sources configured yet.", cut.Markup), RenderTimeout);
        ClickButton(cut, "Add revenue source");
        cut.Find(".rev-form input").Change("New Source");
        cut.Find(".rev-form input[type='date']").Change(DateText(PhilippineTime.Today));
        cut.Find(".rev-form input[pattern]").Change("NEW_SOURCE");

        var pendingSubmit = cut.Find(".rev-form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.True(cut.Find(".rev-modal-actions button[type='submit']").HasAttribute("disabled")), RenderTimeout);
        cut.Find(".rev-modal-actions button[type='submit']").Click();
        api.Verify(client => client.CreateClassificationAsync(It.IsAny<CreateRevenueClassificationCommand>()), Times.Once);

        completion.SetResult(Result<RevenueClassificationDto>.Success(created));
        await pendingSubmit;
    }

    [Fact]
    public void PolicyHistory_PreservesNewestFirstAndLabelsScheduledCurrentAndPrevious()
    {
        var id = Guid.NewGuid();
        var classification = Classification("STALL_RENT", "Stall Rent", true, true,
            Policy("STALL_RENT", "Stall Rent", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt, id), id);
        var future = Policy("STALL_RENT", "Future Stall Rent", PhilippineTime.Today.AddDays(5), RevenueInstrumentType.CashTicket, Guid.NewGuid());
        var current = Policy("STALL_RENT", "Stall Rent", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt, id);
        var previous = Policy("STALL_RENT", "Former Stall Rent", PhilippineTime.Today.AddDays(-30), RevenueInstrumentType.OfficialReceipt, Guid.NewGuid());
        var (cut, api) = RenderPage([classification], id, [future, current, previous]);

        cut.WaitForAssertion(() => Assert.Contains("Stall Rent", cut.Markup), RenderTimeout);
        ClickButton(cut, "Manage");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".rev-history-table tbody tr");
            Assert.Equal(3, rows.Count);
            Assert.Contains("Future Stall Rent", rows[0].TextContent);
            Assert.Contains("Scheduled", rows[0].TextContent);
            Assert.Contains("In effect", rows[1].TextContent);
            Assert.Contains("Previous", rows[2].TextContent);
            Assert.Equal(1, rows.Count(row => row.TextContent.Contains("In effect", StringComparison.Ordinal)));
            Assert.Equal(1, rows.Count(row => row.TextContent.Contains("Scheduled", StringComparison.Ordinal)));
            Assert.Equal(1, rows.Count(row => row.TextContent.Contains("Previous", StringComparison.Ordinal)));
        }, RenderTimeout);
        api.Verify(client => client.GetPolicyHistoryAsync(id), Times.Once);
    }

    [Fact]
    public void FuturePolicyChange_AppendsVersionWithoutReplacingPolicyEffectiveToday()
    {
        var id = Guid.NewGuid();
        var todayPolicy = Policy("MARKET_FEES", "Market Fees", PhilippineTime.Today, RevenueInstrumentType.CashTicket, Guid.NewGuid());
        var futurePolicy = Policy("MARKET_FEES", "Updated Market Fees", PhilippineTime.Today.AddDays(7), RevenueInstrumentType.CashTicket, Guid.NewGuid());
        var item = Classification("MARKET_FEES", "Market Fees", true, true, todayPolicy, id);
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .Returns(() => Task.FromResult(Result<IReadOnlyList<RevenueClassificationDto>>.Success(
                [item])));
        api.Setup(client => client.GetPolicyHistoryAsync(id))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationPolicyDto>>.Success([futurePolicy, todayPolicy]));
        api.Setup(client => client.AppendPolicyAsync(id, It.IsAny<AppendRevenueClassificationPolicyRequest>()))
            .ReturnsAsync(Result<RevenueClassificationPolicyDto>.Success(futurePolicy));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("Market Fees", cut.Markup), RenderTimeout);
        ClickButton(cut, "Manage");
        cut.WaitForAssertion(() => Assert.Contains("Policy history", cut.Markup), RenderTimeout);
        ClickButton(cut, "Schedule policy change");
        var fields = cut.FindAll(".rev-form input");
        fields[1].Change("Updated Market Fees");
        fields[2].Change(DateText(PhilippineTime.Today.AddDays(7)));
        cut.Find(".rev-form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Policy version added", cut.Markup);
            Assert.Contains("Market Fees", cut.Find(".rev-table tbody").TextContent);
            Assert.Contains("Updated Market Fees", cut.Find(".rev-history-table").TextContent);
            Assert.Contains("Scheduled", cut.Find(".rev-history-table tbody tr").TextContent);
        }, RenderTimeout);
        api.Verify(client => client.AppendPolicyAsync(id, It.Is<AppendRevenueClassificationPolicyRequest>(request =>
            request.EffectiveDate == PhilippineTime.Today.AddDays(7)
            && request.DisplayName == "Updated Market Fees")), Times.Once);
        api.Verify(client => client.GetPolicyHistoryAsync(id), Times.Exactly(2));
    }

    [Fact]
    public void FailedPolicyChangeAndRetirement_KeepTheirDialogsRecoverable()
    {
        var id = Guid.NewGuid();
        var policy = Policy("MARKET_FEES", "Market Fees", PhilippineTime.Today, RevenueInstrumentType.CashTicket);
        var item = Classification("MARKET_FEES", "Market Fees", true, true, policy, id);
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success([item]));
        api.Setup(client => client.GetPolicyHistoryAsync(id))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationPolicyDto>>.Success([policy]));
        api.Setup(client => client.AppendPolicyAsync(id, It.IsAny<AppendRevenueClassificationPolicyRequest>()))
            .ReturnsAsync(Result<RevenueClassificationPolicyDto>.Failure("A policy already exists for this date.", 409));
        api.Setup(client => client.RetireAsync(id))
            .ReturnsAsync(Result<bool>.Failure("The source could not be retired.", 500));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("Market Fees", cut.Markup), RenderTimeout);
        ClickButton(cut, "Manage");
        cut.WaitForAssertion(() => Assert.Contains("Policy history", cut.Markup), RenderTimeout);
        ClickButton(cut, "Schedule policy change");
        var inputs = cut.FindAll(".rev-form input");
        inputs[1].Change("Market Fees revised");
        inputs[2].Change(DateText(PhilippineTime.Today.AddDays(1)));
        cut.Find(".rev-form").Submit();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("A policy already exists for this date.", cut.Markup);
            Assert.Single(cut.FindAll(".rev-modal"));
        }, RenderTimeout);

        cut.Find(".rev-modal-close").Click();
        ClickButton(cut, "Retire source");
        cut.Find(".rev-modal-actions .rev-button-danger").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("The source could not be retired.", cut.Markup);
            Assert.Single(cut.FindAll(".rev-modal"));
        }, RenderTimeout);
    }

    [Fact]
    public void FailedCreate_LeavesFormOpenWithRecoverableServerMessage()
    {
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success([]));
        api.Setup(client => client.CreateClassificationAsync(It.IsAny<CreateRevenueClassificationCommand>()))
            .ReturnsAsync(Result<RevenueClassificationDto>.Failure("This internal reference is already in use.", 409));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("No revenue sources configured yet.", cut.Markup), RenderTimeout);
        ClickButton(cut, "Add revenue source");
        cut.Find(".rev-form input").Change("Source");
        cut.Find(".rev-form input[type='date']").Change(DateText(PhilippineTime.Today));
        cut.Find(".rev-form input[pattern]").Change("SOURCE_1");
        cut.Find(".rev-form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("This internal reference is already in use.", cut.Markup);
            Assert.Single(cut.FindAll(".rev-modal"));
        }, RenderTimeout);
    }

    [Fact]
    public void Retirement_RequiresConfirmationAndRetainsRetiredRowAndHistory()
    {
        var id = Guid.NewGuid();
        var item = Classification("TABO", "Tabo", true, true,
            Policy("TABO", "Tabo", PhilippineTime.Today, RevenueInstrumentType.CashTicket), id);
        var api = new Mock<IRevenueClassificationsApiClient>();
        var active = true;
        api.Setup(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .Returns(() => Task.FromResult(Result<IReadOnlyList<RevenueClassificationDto>>.Success([
                Classification("TABO", "Tabo", active, true, item.EffectivePolicy, id)])));
        api.Setup(client => client.GetPolicyHistoryAsync(id))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationPolicyDto>>.Success([item.EffectivePolicy!]));
        api.Setup(client => client.RetireAsync(id)).Callback(() => active = false)
            .ReturnsAsync(Result<bool>.Success(true));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("Tabo", cut.Markup), RenderTimeout);
        ClickButton(cut, "Manage");
        cut.WaitForAssertion(() => Assert.Contains("Policy history", cut.Markup), RenderTimeout);
        ClickButton(cut, "Retire source");
        Assert.Contains("Retire revenue source?", cut.Markup);
        api.Verify(client => client.RetireAsync(id), Times.Never);
        cut.Find(".rev-modal-actions .rev-button-danger").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Revenue source retired", cut.Markup);
            Assert.Contains("Retired", cut.Find(".rev-table tbody").TextContent);
            Assert.Contains("Policy history", cut.Markup);
            Assert.DoesNotContain("Schedule policy change", cut.Markup);
            Assert.DoesNotContain("Reactivate", cut.Markup);
            Assert.DoesNotContain("Delete", cut.Markup);
        }, RenderTimeout);
        api.Verify(client => client.RetireAsync(id), Times.Once);
    }

    [Fact]
    public void ListFailure_ShowsRetryAndRetryLoadsAgain()
    {
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.SetupSequence(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Failure("Unavailable", 500))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success([]));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("Couldn’t load revenue setup", cut.Markup), RenderTimeout);
        ClickButton(cut, "Try again");
        cut.WaitForAssertion(() => Assert.Contains("No revenue sources configured yet.", cut.Markup), RenderTimeout);
        api.Verify(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("SuperAdmin", true)]
    [InlineData("Admin", false)]
    public void Settings_OnlyOffersRevenueSetupLinkToHead(string role, bool actionable)
    {
        RegisterSettingsPageServices();
        this.AddTestAuthorization().SetAuthorized("staff").SetRoles(role);
        var cut = RenderComponent<Settings>();

        cut.WaitForAssertion(() => Assert.Contains("Revenue Setup", cut.Markup), RenderTimeout);
        var revenueLinks = cut.FindAll("a[href='/settings/revenue']");
        if (actionable)
        {
            Assert.Single(revenueLinks);
            Assert.Contains("Manage revenue sources", revenueLinks[0].TextContent);
        }
        else
        {
            Assert.Empty(revenueLinks);
            Assert.Contains("Managed by the EEMO Head", cut.Markup);
            Assert.Contains("aria-disabled=\"true\"", cut.Markup);
        }
    }

    [Fact]
    public async Task ApiClient_FormatsAsOfDateInvariantly()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            HttpRequestMessage? captured = null;
            var handler = new CapturingHandler(request =>
            {
                captured = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://stalltrack.test/") };
            var client = new RevenueClassificationsApiClient(http);

            var result = await client.GetClassificationsAsync(new DateOnly(2026, 9, 30));

            Assert.True(result.IsSuccess);
            Assert.Equal("?asOf=2026-09-30", captured?.RequestUri?.Query);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private (IRenderedComponent<RevenueSetup> Cut, Mock<IRevenueClassificationsApiClient> Api) RenderPage(
        IReadOnlyList<RevenueClassificationDto> items,
        Guid? historyId = null,
        IReadOnlyList<RevenueClassificationPolicyDto>? history = null)
    {
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success(items));
        if (historyId.HasValue)
            api.Setup(client => client.GetPolicyHistoryAsync(historyId.Value))
                .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationPolicyDto>>.Success(history ?? []));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        return (RenderComponent<RevenueSetup>(), api);
    }

    private void RegisterGlobalPageServices()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        var municipalities = new Mock<IMunicipalitiesApiClient>();
        municipalities.Setup(api => api.GetCurrentBrandingAsync())
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(municipalities.Object));
    }

    private void RegisterSettingsPageServices()
    {
        RegisterGlobalPageServices();
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(api => api.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success([]));
        Services.AddSingleton(facilities.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));

        var settings = new Mock<ISettingsApiClient>();
        settings.Setup(api => api.GetSystemSettingsAsync()).ReturnsAsync(Result<SystemSettingsDto>.Success(
            new SystemSettingsDto(
                new OfficeProfileDto("EEMO", "Cantilan", "Surigao del Sur", "StallTrack", "EEMO Head"),
                new SecurityPolicyDto(15, 7, 5, 15, ["SuperAdmin", "Admin"]),
                new CollectionRulesDto(1, 12, 24, 12, 3, "Asia/Manila"),
                new SystemInfoDto("StallTrack", "1.0", "Test", "Asia/Manila", DateTime.UtcNow),
                [])));
        Services.AddSingleton(settings.Object);

        var mfa = new Mock<IMfaApiClient>();
        mfa.Setup(api => api.GetMfaStatusAsync()).ReturnsAsync(Result<MfaStatusDto>.Success(
            new MfaStatusDto(false, false, null, 0)));
        Services.AddSingleton(mfa.Object);
        Services.AddSingleton<ITenantUsageApiClient>(Mock.Of<ITenantUsageApiClient>(api =>
            api.GetUsageAsync() == Task.FromResult(Result<EEMOCantilanSDS.Application.Dtos.SystemHealth.TenantUsageDto>.Failure("Unavailable", 500))));
        Services.AddSingleton<IDatabaseHealthApiClient>(Mock.Of<IDatabaseHealthApiClient>(api =>
            api.GetHealthAsync() == Task.FromResult(Result<EEMOCantilanSDS.Application.Dtos.SystemHealth.DatabaseHealthDto>.Failure("Unavailable", 500))));

        Services.AddLogging();
        Services.AddSingleton<AuthService>(provider => new AuthService(
            provider.GetRequiredService<IJSRuntime>(),
            provider.GetRequiredService<NavigationManager>(),
            new AuthStateProvider(new HttpContextAccessor()),
            new TokenService(),
            provider.GetRequiredService<ILogger<AuthService>>()));
    }

    private static RevenueClassificationDto Classification(
        string code,
        string displayName,
        bool active,
        bool hasPolicyVersions,
        RevenueClassificationPolicyDto? effectivePolicy,
        Guid? id = null) => new(id ?? Guid.NewGuid(), code, active, hasPolicyVersions, effectivePolicy);

    private static RevenueClassificationPolicyDto Policy(
        string code,
        string displayName,
        DateOnly effectiveDate,
        RevenueInstrumentType? instrument,
        Guid? id = null) => new(id ?? Guid.NewGuid(), effectiveDate, displayName, null, instrument,
            new DateTime(2026, 9, 23, 4, 0, 0, DateTimeKind.Utc), "head@eemo.test");

    private static void ClickButton(IRenderedComponent<RevenueSetup> cut, string text) =>
        cut.FindAll("button").First(button => button.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
