using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class FollowUpAccountReviewTests : TestContext
{
    private readonly Mock<IObligationsApiClient> _accounts = new();
    private readonly Mock<IPaymentsApiClient> _payments = new();
    private readonly Mock<IDailyCollectionApiClient> _daily = new();
    public FollowUpAccountReviewTests()
    {
        Services.AddSingleton(_accounts.Object); Services.AddSingleton(_payments.Object); Services.AddSingleton(_daily.Object);
        var fineApi = new Mock<IPenaltiesApiClient>();
        fineApi.Setup(x => x.GetAssessmentsAsync(It.IsAny<EEMOCantilanSDS.Domain.Entities.Revenue.FineAccountKind?>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([]));
        fineApi.Setup(x => x.GetFinePoliciesAsync()).ReturnsAsync(Result<IReadOnlyList<FinePolicyDto>>.Success([]));
        fineApi.Setup(x => x.GetDefinitionsAsync()).ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Success([]));
        fineApi.Setup(x => x.GetFineAccountAsync(It.IsAny<FineAccountRef>())).ReturnsAsync(Result<FineAccountOption>.NotFound());
        Services.AddSingleton(fineApi.Object);
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>()); Services.AddSingleton<FacilityState>();
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>()); Services.AddSingleton<BrandingState>();
        Services.AddSingleton(Mock.Of<IStallsApiClient>()); Services.AddSingleton(Mock.Of<ISetupApiClient>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
    private static ObligationAccountDto Account(ObligationKind kind = ObligationKind.KanmanggaySpaceRental) => new(
        Guid.NewGuid(), kind, "Kanmanggay", Guid.Empty, "Maria de la Cruz", null, null, "4", null, null,
        new(2023, 6, 1), new(2026, 9, 30), 1500m, new(2026, 1, 1), 4200m, 1200m, 3000m,
        OccupancyArrangement.SignedContract, "KM-CONTRACT", "Maria de la Cruz", "Maria Contract", new(2023, 6, 4), 3);
    private static ObligationQuoteDto Quote(ObligationAccountDto account, DateOnly date, decimal bill, decimal settled) => new(
        account.Id, Guid.NewGuid(), account.Kind, account.KindLabel, account.SubjectLabel, date, bill, settled, bill - settled,
        Guid.Empty, account.PayerName, Guid.NewGuid(), bill > settled, account.Event);
    private static FollowUpReviewContext ContextFor(ObligationAccountDto account) => new(null, new(account.Id, Guid.Empty,
        account.PayerName, account.SubjectLabel, new(account.Kind == ObligationKind.FiestaArawLotRental ? FollowUpScopeKind.EventLot : FollowUpScopeKind.MonthlySpace,
        account.Kind == ObligationKind.FiestaArawLotRental ? "FIESTA_ARAW_LOT_RENTAL" : "KANMANGGAY_SPACE_RENTAL", account.KindLabel),
        account.ActiveFrom, account.AssessedToDate, account.CollectedToDate, account.OutstandingToDate, "High", account.Event, "/operations/kanmanggay/manage-space-holders"), new(2026, 9, 30));
    private void Serve(ObligationAccountDto account, params ObligationQuoteDto[] periods)
    {
        _accounts.Setup(x => x.GetAccountsAsync(account.Kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success([account]));
        _accounts.Setup(x => x.GetRegisterAsync(account.Id)).ReturnsAsync(Result<IReadOnlyList<ObligationQuoteDto>>.Success(periods));
    }
    [Fact]
    public void ActualPeriodsAcrossYearsReconcileAndNeverGenerateMissingYearsOrReceipts()
    {
        var account = Account();
        Serve(account, Quote(account,new(2023,6,1),1000,0), Quote(account,new(2025,7,1),1200,200),
            Quote(account,new(2026,1,1),1500,500), Quote(account,new(2026,2,1),500,500));
        var cut = RenderComponent<FollowUpAccountReview>(p => p.Add(x=>x.Context,ContextFor(account)));
        Assert.Contains("₱3,000.00",cut.Find(".review-summary").TextContent);
        Assert.Equal(3,cut.FindAll("section[aria-label='Outstanding billing periods'] > details").Count(d=>d.TextContent.Contains("with balance")));
        Assert.DoesNotContain("2024",cut.Markup);
        Assert.Contains("June 2023",cut.Markup); Assert.Contains("July 2025",cut.Markup);
        Assert.Contains("Unpaid",cut.Markup); Assert.Contains("Partial",cut.Markup); Assert.Contains("Paid",cut.Markup);
        Assert.Contains("Settled assessed periods",cut.Markup);
        Assert.Contains("Closed",cut.Markup); Assert.Contains("Maria Contract",cut.Markup);
        Assert.Contains("Receipt allocations and correction history are unavailable here",cut.Markup);
        Assert.Contains("No saved fine assessments",cut.Markup);
        Assert.DoesNotContain("Current read",cut.Markup);
        Assert.DoesNotContain("Account / follow-up status",cut.Markup);
        Assert.Empty(cut.FindAll(".review-detail-row"));
        Assert.DoesNotContain("SRC-",cut.Markup);
        Assert.Empty(cut.FindAll("[role='alert']"));
        Assert.Empty(_payments.Invocations);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("account-review-many-years",cut.Markup);
    }
    [Fact]
    public void MismatchedAccountTotalWarnsAndPreventsPaymentTransition()
    {
        var account=Account(); Serve(account,Quote(account,new(2023,6,1),1000,0));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,ContextFor(account)).Add(x=>x.ActionLabel,"Record payment"));
        Assert.Contains("do not reconcile",cut.Find("[role='alert']").TextContent);
        Assert.True(cut.Find(".btn-primary").HasAttribute("disabled"));
    }
    [Fact]
    public void WrongSourceAccountPeriodsFailClosed()
    {
        var account=Account(); Serve(account,Quote(account,new(2023,6,1),3000,0) with { AccountId=Guid.NewGuid() });
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,ContextFor(account)));
        Assert.Contains("different source account",cut.Find("[role='alert']").TextContent);
        Assert.Empty(cut.FindAll(".review-summary"));
        _accounts.Verify(x=>x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()),Times.Never);
    }
    [Fact]
    public void EventAccountUsesOnlyItsRecordedEventAndNoMonthlyContractGrid()
    {
        var account=Account(ObligationKind.FiestaArawLotRental) with { Event=LotRentalEvent.Fiesta,EventDate=new(2026,8,15),ContractName=null,ContractYears=null,ContractReference=null,ContractEffectiveDate=null };
        Serve(account,Quote(account,new(2026,8,15),4200,1200));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,ContextFor(account)));
        Assert.Contains("Event obligation",cut.Markup); Assert.Contains("Fiesta",cut.Markup);
        Assert.DoesNotContain("Recorded duration",cut.Markup);
        Assert.DoesNotContain("12-month",cut.Markup);
        Assert.Single(cut.FindAll("tbody tr"));
    }
    [Fact]
    public void NpmUsesEligibleDateReadWithOriginalTermAndNeverGuessesExcusedDays()
    {
        var id=Guid.NewGuid(); var term=Guid.NewGuid();
        var item=new FollowUpItemDto(1,"High","Unpaid days","npm",FacilityCode.NPM,"Daily","Vendor","3",60,false,"October 2026","Pending","View","/profile/npm/3",id,ContractId:term);
        _payments.Setup(x=>x.GetOutstandingMonthsAsync(id,term,null,null)).ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([new("2026-10",PaymentStatus.Partial,90,30,60,null,null)]));
        _daily.Setup(x=>x.GetSettleableNpmDaysAsync(id,2026,10,term)).ReturnsAsync(Result<IReadOnlyList<SettleableNpmDayDto>>.Success([new(new(2026,10,5),30),new(new(2026,10,7),30)]));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,new(item,null,new(2026,10,10))));
        cut.Find(".review-period-toggle").Click();
        cut.FindAll("button").Single(b=>b.TextContent=="Review eligible days").Click();
        Assert.Contains("Oct 5, 2026",cut.Markup); Assert.Contains("Oct 7, 2026",cut.Markup);
        Assert.DoesNotContain("Oct 6, 2026",cut.Markup);
        Assert.Contains("Month-goal adjustments",cut.Markup);
        Assert.Equal(2,cut.FindAll(".review-day-table tbody tr").Count);
        Assert.Contains("Billing basis",cut.Markup);
        Assert.Contains("Follow-up reason",cut.Markup);
        Assert.Contains("Coverage",cut.Markup);
        Assert.DoesNotContain("Account / follow-up status",cut.Markup);
        Assert.DoesNotContain("Active with unpaid balances",cut.Markup);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("account-review-npm",cut.Markup);
        cut.FindAll("button").Single(b=>b.TextContent=="Review eligible days").Click();
        Assert.Empty(cut.FindAll(".review-day-table"));
        _daily.Verify(x=>x.GetSettleableNpmDaysAsync(id,2026,10,term),Times.Once);
    }
    [Theory]
    [InlineData("missingor")]
    [InlineData("misc")]
    [InlineData("excused")]
    public void OperationalReviewNeverTurnsReceiptOrExcusedItemIntoDebt(string kind)
    {
        var item=new FollowUpItemDto(1,"Normal","Review",""+kind,FacilityCode.NPM,"Service","Name","Ref",null,kind=="excused","October 2026","Review","View","/npm",Guid.NewGuid());
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,new(item,null,new(2026,10,10))));
        Assert.Contains("Review item",cut.Markup);
        Assert.Empty(cut.FindAll(".review-summary")); Assert.Empty(_payments.Invocations);
        Assert.DoesNotContain("Fine outstanding",cut.Markup);
    }
    [Fact]
    public void MarketWideCoverageGapIsAnOperationalItemNotARegisteredAccount()
    {
        var item = new FollowUpItemDto(1,"High","NPM missed collection","npm",FacilityCode.NPM,"Daily stall","New Public Market","9 missed days",null,false,"October 2026","Daily coverage gap","View","/npm",null);
        var cut = RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,new(item,null,new(2026,10,10))));
        Assert.Contains("Review item",cut.Markup);
        Assert.Empty(cut.FindAll(".review-summary")); Assert.Empty(_payments.Invocations);
    }
    [Fact]
    public void FailedReadIsNotAnEmptyOrZeroBalanceAndRetryUsesSameIdentity()
    {
        var account=Account();
        _accounts.Setup(x=>x.GetAccountsAsync(account.Kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Failure("Unavailable"));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,ContextFor(account)));
        Assert.NotNull(cut.Find("[role='alert']")); Assert.Empty(cut.FindAll(".review-summary"));
        Serve(account,Quote(account,new(2026,1,1),3000,0));
        cut.FindAll("button").Single(b=>b.TextContent=="Try again").Click();
        Assert.Empty(cut.FindAll("[role='alert']")); Assert.Contains("₱3,000.00",cut.Markup);
    }
    [Fact]
    public void SelectedPeriodIsCarriedWithoutPostingAndEscapeClosesReview()
    {
        var account = Account(); Serve(account, Quote(account,new(2023,6,1),3000,0));
        DateOnly? selected = null; var closed = 0;
        var cut = RenderComponent<FollowUpAccountReview>(p => p.Add(x=>x.Context,ContextFor(account))
            .Add(x=>x.OnPeriodSelected, date=>selected=date).Add(x=>x.OnClose,()=>closed++));
        cut.Find(".review-period-toggle").Click();
        Assert.Equal(new DateOnly(2023,6,1),selected);
        Assert.Single(cut.FindAll(".review-detail-row"));
        cut.Find(".review-period-toggle").Click();
        Assert.Empty(cut.FindAll(".review-detail-row"));
        cut.Find("[role='dialog']").KeyDown("Escape"); Assert.Equal(1,closed);
        _accounts.Verify(x=>x.CreateAccountAsync(It.IsAny<CreateObligationAccountRequest>()),Times.Never);
    }
    [Fact]
    public void ForbiddenReadIsExplicitAndNeverRendersMoney()
    {
        var account=Account();
        _accounts.Setup(x=>x.GetAccountsAsync(account.Kind)).ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Forbidden());
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,ContextFor(account)));
        Assert.Contains("not authorized",cut.Find("[role='alert']").TextContent);
        Assert.Empty(cut.FindAll(".review-summary"));
    }
    [Fact]
    public void SettledAccountCannotStartAnotherPaymentAndContractAlertPrioritizesContract()
    {
        var id=Guid.NewGuid();
        var item=new FollowUpItemDto(1,"Normal","Expiring","contract",FacilityCode.ICE,"Monthly","Name","4",null,false,"October 2026","Expiring","View","/profile/ice/4",id);
        _payments.Setup(x=>x.GetOutstandingMonthsAsync(id,null,null,null)).ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([]));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,new(item,null,new(2026,10,10))).Add(x=>x.ActionLabel,"Record payment"));
        Assert.Empty(cut.FindAll(".review-summary"));
        Assert.Contains("Contract review",cut.Markup);
        Assert.True(cut.Find(".btn-primary").HasAttribute("disabled"));
    }
}
