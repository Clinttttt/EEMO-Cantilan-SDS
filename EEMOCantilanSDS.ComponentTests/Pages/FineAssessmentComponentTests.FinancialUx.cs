using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;
public sealed partial class FineAssessmentComponentTests
{
    [Fact] public void MultipleFineStatesStaySeparateAndDoNotOfferSettledPayment()
    {
        Policy();
        var row=Row();
        var settled=Row() with { Settled=120, Remaining=0, Status="Settled" };
        var partial=Row() with { Settled=40,Remaining=80,Status="Outstanding",Penalty="Long approved penalty title with several words for a genuine separately recorded incident" };
        _api.Setup(x=>x.GetAssessmentsAsync(_account.Kind,_account.AccountId,null)).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([row,partial,settled]));
        var cut=RenderComponent<AccountFineAssessments>(p=>p.Add(x=>x.Account,_account));
        cut.WaitForAssertion(()=>Assert.Equal(3,cut.FindAll(".fine-row").Count));
        Assert.Contains("₱40.00",cut.Markup); Assert.Contains("₱80.00",cut.Markup);
        _api.Verify(x=>x.QuoteFineAsync(settled.Id),Times.Never);
        Assert.Empty(cut.FindAll("table")); Assert.Contains("Assess fine",cut.Markup);
        ExportFixture("fine-compact-many",cut.Markup);
    }
    [Fact] public void AccountFineListHasCompactAmountsDetailsAndQuotedPayment()
    {
        var row=Row() with { RelatedPeriod=new(new(EEMOCantilanSDS.Domain.Entities.Revenue.FineAffectedPeriodKind.ObligationPeriod,new(2026,9,1),new(2026,9,1),Guid.NewGuid()),"September 2026") };
        _api.Setup(x=>x.GetAssessmentsAsync(_account.Kind,_account.AccountId,null)).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([row]));
        _api.Setup(x=>x.QuoteFineAsync(row.Id)).ReturnsAsync(Result<FineSettlementQuoteDto>.Success(new(row.Id,_account,row.Occupant,row.Source,row.Unit,120,0,120,"Outstanding",1,"quote",RevenueInstrumentType.OfficialReceipt,true,null)));
        _api.Setup(x=>x.GetFineSettlementHistoryAsync(row.Id)).ReturnsAsync(Result<IReadOnlyList<FineSettlementHistoryDto>>.Success([]));
        Guid? payment=null;
        var cut=RenderComponent<AccountFineAssessments>(p=>p.Add(x=>x.Account,_account).Add(x=>x.OnPay,id=>payment=id));
        cut.WaitForAssertion(()=>Assert.Contains("Record payment",cut.Markup));
        Assert.Empty(cut.FindAll("table")); Assert.Single(cut.FindAll(".fine-row"));
        Assert.Equal(new[]{"Assessed","Collected","Remaining"},cut.FindAll(".fine-amounts dt").Select(x=>x.TextContent));
        Assert.Contains("September 2026",cut.Markup);
        cut.FindAll("button").Single(x=>x.TextContent=="View details").Click();
        Assert.Contains(row.GoverningReference,cut.Markup);
        cut.FindAll("button").Single(x=>x.TextContent=="Record payment").Click(); Assert.Equal(row.Id,payment);
        ExportFixture("fine-compact-details",cut.Markup);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void AccountPaymentFailsClosedWhenQuoteIsUnavailableOrIneligible(bool unavailable)
    {
        var row=Row();
        _api.Setup(x=>x.GetAssessmentsAsync(_account.Kind,_account.AccountId,null)).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([row]));
        _api.Setup(x=>x.QuoteFineAsync(row.Id)).ReturnsAsync(unavailable ? Result<FineSettlementQuoteDto>.Failure("Unavailable") : Result<FineSettlementQuoteDto>.Success(new(row.Id,_account,row.Occupant,row.Source,row.Unit,120,0,120,"Outstanding",1,"quote",RevenueInstrumentType.OfficialReceipt,false,"Source authority unavailable.")));
        var cut=RenderComponent<AccountFineAssessments>(p=>p.Add(x=>x.Account,_account));
        cut.WaitForAssertion(()=>Assert.Contains(unavailable ? "Payment eligibility is unavailable" : "Source authority unavailable",cut.Markup));
        Assert.DoesNotContain("Record payment",cut.Markup); Assert.Contains("₱120.00",cut.Markup);
    }
    [Fact] public void GuidedPaymentKeepsSavedAssessmentWhenPaymentIsCancelled()
    {
        var policy=Policy(); var row=Row();
        _api.Setup(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>())).ReturnsAsync(Result<FineAssessmentDto>.Success(row));
        _api.Setup(x=>x.QuoteFineAsync(row.Id)).ReturnsAsync(Result<FineSettlementQuoteDto>.Failure("Unavailable"));
        var cut=RenderComponent<FineAssessmentsRegister>();
        cut.FindAll("button").Single(x=>x.TextContent=="Assess fine").Click();
        var drawer=cut.FindComponent<FineAssessmentDrawer>();
        _api.Setup(x=>x.SearchFineAccountsAsync(It.IsAny<string>())).ReturnsAsync(Result<IReadOnlyList<FineAccountOption>>.Success([new(_account,"Liable holder","Space 4","Kanmanggay",false)]));
        drawer.Find("input").Change("Liable");
        drawer.FindAll("button").Single(x=>x.TextContent.Trim()=="Search").Click();
        drawer.WaitForAssertion(()=>Assert.Contains("Liable holder",drawer.Markup));
        drawer.Find(".fine-result").Click();
        drawer.Find("select").Change(policy.Id.ToString()); ChooseOther(drawer,"Actual incident"); drawer.Find("input[type='number']").Change("120");
        drawer.Find("form").Submit(); drawer.Find("form").Submit();
        cut.FindAll("button").Single(x=>x.TextContent=="Record payment now").Click();
        cut.WaitForAssertion(()=>Assert.Single(cut.FindComponents<FinePaymentDrawer>()));
        Assert.Empty(cut.FindComponents<FineAssessmentDrawer>());
        var payment=cut.FindComponent<FinePaymentDrawer>(); Assert.Equal(row.Id,payment.Instance.AssessmentId);
        payment.FindAll("button").Last(x=>x.TextContent.Trim()=="Close").Click();
        Assert.Empty(cut.FindComponents<FinePaymentDrawer>());
        _api.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Once);
        _api.Verify(x=>x.PostFineDraftAsync(It.IsAny<Guid>(),It.IsAny<PostEcfCollectionDraftRequest>()),Times.Never);
    }
}
