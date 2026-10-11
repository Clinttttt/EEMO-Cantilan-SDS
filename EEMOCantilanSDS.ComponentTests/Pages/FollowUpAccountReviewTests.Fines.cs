using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed partial class FollowUpAccountReviewTests
{
    private static readonly DateOnly ReviewDate=new(2026,10,10);
    private (FollowUpReviewContext Context,FineAccountRef Account) FacilityFine(FacilityCode code=FacilityCode.NPM,bool historical=false)
    {
        var stall=Guid.NewGuid(); var account=new FineAccountRef(FineAccountKind.FacilityContract,Guid.NewGuid());
        var term=historical ? account.AccountId : (Guid?)null;
        var item=new FollowUpItemDto(1,"High","Delinquent","delinquent",code,"Monthly","Liable occupant","3",60,false,"September 2026","Unpaid","View","/source",stall,ContractId:term);
        _payments.Setup(x=>x.GetOutstandingMonthsAsync(stall,term,null,null)).ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([new("2026-09",PaymentStatus.Partial,90,30,60,null,null)]));
        var option=new FineAccountOption(account,"Liable occupant","3","Rental source",historical,code);
        _fineApi.Setup(x=>x.ResolveFacilityFineAccountAsync(stall,code,ReviewDate,term)).ReturnsAsync(Result<FineAccountOption>.Success(option));
        _fineApi.Setup(x=>x.GetFineAccountAsync(account)).ReturnsAsync(Result<FineAccountOption>.Success(option));
        return (new(item,null,ReviewDate),account);
    }
    private FinePolicyDto FinePolicy(FacilityCode code)
    {
        var definition=new PenaltyDefinitionDto(Guid.NewGuid(),"AUTHORIZED","Approved incident",null,GovernedServiceBasis.DirectApprovedAmount,null,200m,true,new(2026,1,1));
        var policy=new FinePolicyDto(Guid.NewGuid(),new(definition.VersionId,FineAccountKind.FacilityContract,code,"AUTHORITY",FinePenaltyDateBasis.AssessmentDate,false,true));
        _fineApi.Setup(x=>x.GetFinePoliciesAsync()).ReturnsAsync(Result<IReadOnlyList<FinePolicyDto>>.Success([policy]));
        _fineApi.Setup(x=>x.GetDefinitionsAsync()).ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Success([definition]));
        return policy;
    }
    [Theory]
    [InlineData(FacilityCode.NPM)] [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.TCC)]
    [InlineData(FacilityCode.BBQ)] [InlineData(FacilityCode.ICE)]
    public void VerifiedLiveAccountUsesFineReadAndLeavesNullRentalTarget(FacilityCode code)
    {
        var source=FacilityFine(code); FinePolicy(code);
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        Assert.Equal(source.Account,cut.FindComponent<AccountFineAssessments>().Instance.Account);
        Assert.Single(cut.FindAll("button"),x=>x.TextContent=="Assess fine");
        _payments.Verify(x=>x.GetOutstandingMonthsAsync(source.Context.FacilityItem!.StallId!.Value,null,null,null),Times.Once);
        _fineApi.Verify(x=>x.GetAssessmentsAsync(source.Account.Kind,source.Account.AccountId,null),Times.Once);
    }
    [Fact]
    public void HistoricalFineKeepsTheExplicitOriginalContract()
    {
        var source=FacilityFine(historical:true); FinePolicy(FacilityCode.NPM);
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        cut.FindAll("button").Single(x=>x.TextContent=="Assess fine").Click();
        Assert.Equal(source.Account,cut.FindComponent<FineAssessmentDrawer>().Instance.Account);
        Assert.Single(cut.FindAll("[role=dialog]"));
        _payments.Verify(x=>x.GetOutstandingMonthsAsync(source.Context.FacilityItem!.StallId!.Value,source.Account.AccountId,null,null),Times.Once);
    }
    [Theory]
    [InlineData("Admin")] [InlineData("Collector")]
    public void NonHeadCannotOpenInContextAssessment(string role)
    {
        this.AddTestAuthorization().SetAuthorized(role).SetRoles(role);
        var source=FacilityFine(); FinePolicy(FacilityCode.NPM);
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        Assert.DoesNotContain(cut.FindAll("button"),x=>x.TextContent=="Assess fine");
        Assert.Empty(cut.FindComponents<FineAssessmentDrawer>());
        _fineApi.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
    }
    [Fact]
    public void VerifiedAccountWithoutPolicyHasDistinctEligibilityMessage()
    {
        var source=FacilityFine();
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        Assert.Contains("No approved fine policy is configured for this source",cut.Markup);
        Assert.DoesNotContain("Liable occupancy could not be verified",cut.Markup);
        Assert.DoesNotContain(cut.FindAll("button"),x=>x.TextContent=="Assess fine");
    }
    [Fact]
    public void AmbiguousAccountCannotOfferAnAssessmentAction()
    {
        var source=FacilityFine(); FinePolicy(FacilityCode.NPM);
        _fineApi.Setup(x=>x.ResolveFacilityFineAccountAsync(It.IsAny<Guid>(),It.IsAny<FacilityCode>(),It.IsAny<DateOnly>(),It.IsAny<Guid?>())).ReturnsAsync(Result<FineAccountOption>.Failure("Ambiguous current occupancy",ResultStatus.Conflict));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        Assert.Contains("Reload the queue or open the source account",cut.Markup);
        Assert.Empty(cut.FindComponents<AccountFineAssessments>()); Assert.Empty(cut.FindComponents<FineAssessmentDrawer>());
    }
    [Fact]
    public void ExplicitSourceRecoveryVerifiesChosenContractAndPreservesRentalTarget()
    {
        var source=FacilityFine();FinePolicy(FacilityCode.NPM);
        var stall=source.Context.FacilityItem!.StallId!.Value;
        var option=new FineAccountOption(source.Account,"Original liable occupant","3","New Public Market",true,FacilityCode.NPM,"Fish Area",new(2023,6,7),new(2026,6,6),"Lapsed");
        _fineApi.Setup(x=>x.ResolveFacilityFineAccountAsync(stall,FacilityCode.NPM,ReviewDate,null)).ReturnsAsync(Result<FineAccountOption>.Failure("Ambiguous",ResultStatus.Conflict));
        _fineApi.Setup(x=>x.GetFacilityFineAccountsAsync(stall,FacilityCode.NPM)).ReturnsAsync(Result<IReadOnlyList<FineAccountOption>>.Success([option]));
        _fineApi.Setup(x=>x.ResolveFacilityFineAccountAsync(stall,FacilityCode.NPM,ReviewDate,source.Account.AccountId)).ReturnsAsync(Result<FineAccountOption>.Success(option));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        var summary=cut.Find(".review-summary").TextContent;
        cut.FindAll("button").Single(x=>x.TextContent=="Select verified source account").Click();
        Assert.Empty(cut.FindComponents<AccountFineAssessments>());
        cut.FindAll("button").Single(x=>x.TextContent.Contains("Original liable occupant")).Click();
        Assert.Equal(source.Account,cut.FindComponent<AccountFineAssessments>().Instance.Account);
        Assert.Equal(summary,cut.Find(".review-summary").TextContent);
        _payments.Verify(x=>x.GetOutstandingMonthsAsync(stall,null,null,null),Times.Once);
        Assert.Null(source.Context.FacilityItem.ContractId);
        _fineApi.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
    }
    [Fact]
    public void UnverifiedOrWrongFacilityCannotReadOrAssessFineButRentalStillLoads()
    {
        var source=FacilityFine();
        _fineApi.Setup(x=>x.ResolveFacilityFineAccountAsync(It.IsAny<Guid>(),It.IsAny<FacilityCode>(),It.IsAny<DateOnly>(),It.IsAny<Guid?>()))
            .ReturnsAsync(Result<FineAccountOption>.Success(new(source.Account,"Wrong source","3","Other facility",false,FacilityCode.ICE)));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        Assert.Contains("Liable occupancy could not be verified",cut.Markup); Assert.Empty(cut.FindComponents<AccountFineAssessments>());
        Assert.Contains("₱60.00",cut.Find(".review-summary").TextContent);
        _fineApi.Verify(x=>x.GetAssessmentsAsync(It.IsAny<FineAccountKind?>(),It.IsAny<Guid?>(),It.IsAny<string?>()),Times.Never);
    }
    [Fact]
    public void SaveFineReturnsToPreservedReviewAndRefreshesOnlyFinePosition()
    {
        var source=FacilityFine(); var policy=FinePolicy(FacilityCode.NPM); var saved=false;
        var row=new FineAssessmentDto(Guid.NewGuid(),source.Account,"Liable occupant","3","NPM","Approved incident","Recorded event",ReviewDate,ReviewDate,"CASE-REVIEW",120,0,120,"Outstanding",1,"AUTHORITY",DateTime.UtcNow,null);
        _fineApi.Setup(x=>x.GetAssessmentsAsync(source.Account.Kind,source.Account.AccountId,null)).ReturnsAsync(()=>Result<IReadOnlyList<FineAssessmentDto>>.Success(saved ? [row] : []));
        _fineApi.Setup(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>())).Callback(()=>saved=true).ReturnsAsync(Result<FineAssessmentDto>.Success(row));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        var before=cut.Find(".review-summary").TextContent;
        cut.Find(".review-period-toggle").Click();
        cut.FindAll("button").Single(x=>x.TextContent=="Assess fine").Click();
        Assert.Single(cut.FindAll("[role=dialog]")); Assert.Equal(source.Account,cut.FindComponent<FineAssessmentDrawer>().Instance.Account);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("account-review-assess-fine",cut.Markup);
        cut.Find("select").Change(policy.Id.ToString()); cut.Find("textarea").Change("Recorded event"); cut.Find("input[aria-label='Fine amount']").Change("120");
        cut.Find("form").Submit(); SpaceHolderWorkspaceTests.SaveVisualArtifact("account-review-confirm-fine",cut.Markup); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Empty(cut.FindComponents<FineAssessmentDrawer>()));
        Assert.Single(cut.FindAll("[role=dialog]")); Assert.Equal(before,cut.Find(".review-summary").TextContent);
        Assert.Single(cut.FindAll(".review-detail-row")); Assert.Contains("₱120.00",cut.Find(".review-charges").TextContent);
        _payments.Verify(x=>x.GetOutstandingMonthsAsync(source.Context.FacilityItem!.StallId!.Value,null,null,null),Times.Once);
        _fineApi.Verify(x=>x.CreateAssessmentAsync(It.Is<CreateFineAssessmentRequest>(r=>r.Account==source.Account&&r.PolicyId==policy.Id&&r.Amount==120)),Times.Once);
        _fineApi.Verify(x=>x.GetAssessmentsAsync(source.Account.Kind,source.Account.AccountId,null),Times.Exactly(2));
        SpaceHolderWorkspaceTests.SaveVisualArtifact("account-review-saved-fine",cut.Markup);
    }
    [Fact]
    public void CancelFineReturnsToReviewWithoutCreatingOrReloadingRental()
    {
        var source=FacilityFine(); FinePolicy(FacilityCode.NPM);
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        cut.FindAll("button").Single(x=>x.TextContent=="Assess fine").Click();
        cut.FindAll("button").Single(x=>x.TextContent=="Cancel").Click();
        Assert.Empty(cut.FindComponents<FineAssessmentDrawer>()); Assert.Single(cut.FindAll("[role=dialog]"));
        _fineApi.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
        _payments.Verify(x=>x.GetOutstandingMonthsAsync(source.Context.FacilityItem!.StallId!.Value,null,null,null),Times.Once);
    }
    [Fact]
    public async Task FinePaymentReturnRefreshesOnlyChargesAndPreservesRentalDisclosure()
    {
        var source=FacilityFine();
        var id=Guid.NewGuid();
        var row=new FineAssessmentDto(id,source.Account,"Liable occupant","3","NPM","Approved incident","Recorded event",ReviewDate,ReviewDate,"CASE-PAY",120,0,120,"Outstanding",1,"AUTHORITY",DateTime.UtcNow,null);
        _fineApi.Setup(x=>x.GetAssessmentsAsync(source.Account.Kind,source.Account.AccountId,null)).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([row]));
        _fineApi.Setup(x=>x.QuoteFineAsync(id)).ReturnsAsync(Result<FineSettlementQuoteDto>.Success(new(id,source.Account,row.Occupant,row.Source,row.Unit,120,0,120,"Outstanding",1,"token",RevenueInstrumentType.OfficialReceipt,true,null)));
        var cut=RenderComponent<FollowUpAccountReview>(p=>p.Add(x=>x.Context,source.Context));
        cut.WaitForAssertion(()=>Assert.Contains("Record payment",cut.Find(".review-charges").TextContent));
        var before=cut.Find(".review-summary").TextContent;
        cut.Find(".review-period-toggle").Click();
        cut.Find(".review-charges").QuerySelectorAll("button").Single(x=>x.TextContent=="Record payment").Click();
        Assert.Single(cut.FindAll("[role=dialog]"));
        var payment=cut.FindComponent<FinePaymentDrawer>(); Assert.Equal(id,payment.Instance.AssessmentId);
        await cut.InvokeAsync(()=>payment.Instance.OnPaid.InvokeAsync());
        await cut.InvokeAsync(()=>payment.Instance.OnClose.InvokeAsync());
        cut.WaitForAssertion(()=>Assert.Empty(cut.FindComponents<FinePaymentDrawer>()));
        Assert.Equal(before,cut.Find(".review-summary").TextContent); Assert.Single(cut.FindAll(".review-detail-row"));
        _fineApi.Verify(x=>x.GetAssessmentsAsync(source.Account.Kind,source.Account.AccountId,null),Times.Exactly(2));
        _payments.Verify(x=>x.GetOutstandingMonthsAsync(source.Context.FacilityItem!.StallId!.Value,null,null,null),Times.Once);
        _fineApi.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
    }
}
