using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Moq;
namespace EEMOCantilanSDS.ComponentTests.Pages;
public sealed partial class FineAssessmentComponentTests
{
    private DirectFineCategoryDto DirectCategory(bool evidence=false) {
        var c=new DirectFineCategoryDto(Guid.NewGuid(),"Approved direct fine",GovernedServiceBasis.DirectApprovedAmount,null,500m,evidence,evidence,evidence,"ISOLATED TEST authority");
        _api.Setup(x=>x.GetDirectFineOptionsAsync()).ReturnsAsync(Result<DirectFineOptionsDto>.Success(new(new(2026,10,11),[c],"test-scope")));
        _api.Setup(x=>x.QuoteDirectFineAsync(It.IsAny<DirectFineCollectionRequest>())).ReturnsAsync((DirectFineCollectionRequest r)=>Result<DirectFineQuoteDto>.Success(new(r,c.Name,c.GoverningReference,RevenueInstrumentType.OfficialReceipt)));
        return c;
    }
    [Fact] public void DirectFineFormIsAmountFirstWithoutRegistrationOrContract()
    {
        DirectCategory(); var cut=RenderComponent<DirectFineCollectionDrawer>();
        Assert.Equal("number",cut.Find("input").GetAttribute("type"));
        Assert.Contains("Fine amount (₱)",cut.Markup); Assert.DoesNotContain("Search account",cut.Markup);
        // Receipt particulars and reference are offered but optional when the authority does not require them.
        Assert.Contains("Payer name / receipt particulars (optional)",cut.Markup); Assert.Contains("Supporting reference (optional)",cut.Markup);
        Assert.Single(cut.FindAll("input[required]")); Assert.DoesNotContain("Contract",cut.Markup);
        Assert.True(cut.Markup.IndexOf("Fine category",StringComparison.Ordinal) < cut.Markup.IndexOf("Fine amount",StringComparison.Ordinal));
        ExportFixture("direct-fine-form",cut.Markup);
    }
    [Fact] public void AssessDrawerIsSteppedAndShowsAStructuredAccountCardBeforeFineDetails()
    {
        Policy(); var cut=RenderComponent<FineAssessmentDrawer>();
        Assert.Equal("true",cut.Find("li.current").GetAttribute("aria-current")); Assert.Contains("Account",cut.Find("li.current").TextContent);
        _api.Setup(x=>x.SearchFineAccountsAsync(It.IsAny<string>())).ReturnsAsync(Result<IReadOnlyList<FineAccountOption>>.Success(
            [new(_account,"Liable holder","Space 4","Kanmanggay",false,null,"Section A",new DateOnly(2026,1,1),new DateOnly(2026,12,31),"Active")]));
        cut.Find("input").Change("Liable"); cut.FindAll("button").First(x=>x.TextContent.Trim()=="Search").Click();
        cut.WaitForAssertion(()=>Assert.Contains("Liable holder",cut.Markup));
        cut.Find(".fine-result").Click();

        var card=cut.Find(".fine-account");
        foreach(var text in new[]{"Liable holder","Facility / source","Kanmanggay","Stall / unit","Space 4","Area / section","Section A","Recorded term","Jan 1, 2026","Account type","Active"})
            Assert.Contains(text,card.TextContent);
        Assert.Contains("Fine details",cut.Find("li.current").TextContent); Assert.Empty(cut.FindAll(".fine-result"));
        ExportFixture("assess-fine-selected-account",cut.Markup);

        cut.FindAll("button").Single(x=>x.TextContent.Trim()=="Change account").Click();
        Assert.Contains("Account",cut.Find("li.current").TextContent); Assert.Empty(cut.FindAll(".fine-account"));
    }
    private PenaltyRegisterRowDto Collected(string kind,string? payer,string? account,string? facility,string? reason,string status="Posted",decimal amount=100m) =>
        new(Guid.NewGuid(),new DateOnly(2026,10,9),DateTime.UtcNow,payer,kind==PenaltyRegisterKinds.Definition?"Stall 4 · September rent":null,"CODE","Obstruction of walkway",
            $"SRC-2026-{Random.Shared.Next(100000,999999)}",amount,status,status=="Posted"?amount:0m,kind,account,facility,reason);
    [Fact] public void PopulatedWorkspaceRendersEveryTabForReview()
    {
        var policy=Policy(); var assessed=Row() with {Occupant="Maria L. Dela Cruz",Unit="Stall 12",Source="Kanmanggay",Description="Unauthorized extension of stall frontage beyond the assigned line",
            RelatedPeriod=new(new(FineAffectedPeriodKind.FacilityMonth,new(2026,9,1),new(2026,9,30)),"September 2026"),Settled=0m};
        var partial=Row() with {Occupant="Ramon Bautista",Unit="Space 7",Source="Vegetable/Fruits",Description="Late closing",Assessed=200m,Settled=80m,Remaining=120m,Status="Partial"};
        var settled=Row() with {Occupant="Teresita Gomez",Unit="Stall 3",Source="NPM",Description="Improper waste disposal",Assessed=150m,Settled=150m,Remaining=0m,Status="Settled"};
        _api.Setup(x=>x.GetAssessmentsAsync(It.IsAny<FineAccountKind?>(),It.IsAny<Guid?>(),It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([assessed,partial,settled]));
        _api.Setup(x=>x.GetRegisterAsync(It.IsAny<DateOnly>(),It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<PenaltyRegisterRowDto>>.Success([
            Collected(PenaltyRegisterKinds.Direct,"Walk-up payer",null,null,"Obstructing the public walkway during market hours"),
            Collected(PenaltyRegisterKinds.Direct,null,null,null,null,amount:200m),
            Collected(PenaltyRegisterKinds.Assessed,"Teresita Gomez","Teresita Gomez · Stall 3","NPM","Improper waste disposal",amount:150m),
            Collected(PenaltyRegisterKinds.Definition,"Ben Cruz",null,null,null,"Corrected"),
            Collected(PenaltyRegisterKinds.Direct,null,null,null,"Duplicate entry","Reversed")]));
        _api.Setup(x=>x.GetSummaryAsync(It.IsAny<DateOnly>(),It.IsAny<DateOnly>())).ReturnsAsync(Result<PenaltyWorkspaceSummaryDto>.Success(new(new(2026,10,1),new(2026,10,31),450m,300m,240m,2)));
        _api.Setup(x=>x.GetFineSourcesAsync()).ReturnsAsync(Result<IReadOnlyList<FineSourceOption>>.Success([]));
        _api.Setup(x=>x.GetAssessmentHistoryAsync(It.IsAny<Guid>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentEventDto>>.Success([new(Guid.NewGuid(),"Created",0m,120m,Guid.NewGuid(),new DateTime(2026,10,8,3,15,0,DateTimeKind.Utc),"Assessed")]));
        _api.Setup(x=>x.GetFineSettlementHistoryAsync(It.IsAny<Guid>())).ReturnsAsync(Result<IReadOnlyList<FineSettlementHistoryDto>>.Success([]));

        var cut=RenderComponent<Fines>();
        cut.WaitForAssertion(()=>Assert.Equal(5,cut.FindAll("[aria-label='Fines collected'] tbody tr").Count));
        ExportFixture("populated-fines-collections",cut.Markup);
        cut.FindAll("[aria-label='Fines workspace views'] button").Single(x=>x.TextContent=="Assessments").Click();
        cut.WaitForAssertion(()=>Assert.Equal(3,cut.FindAll("[aria-label='Fine assessments'] tbody tr").Count));
        ExportFixture("populated-fines-assessments",cut.Markup);
        cut.FindAll("button").First(x=>x.TextContent=="View details").Click();
        cut.WaitForAssertion(()=>Assert.Contains("Fine details",cut.Markup));
        ExportFixture("populated-fines-details",cut.Markup);
        cut.Find(".eemo-drawer-header-close").Click();
        cut.FindAll("[aria-label='Fines workspace views'] button").Single(x=>x.TextContent=="Penalty settings").Click();
        ExportFixture("populated-fines-settings",cut.Markup);
    }
    [Fact] public void AssessDrawerWithoutAnApprovedPolicyShowsACleanUnavailableState()
    {
        _api.Setup(x=>x.SearchFineAccountsAsync(It.IsAny<string>())).ReturnsAsync(Result<IReadOnlyList<FineAccountOption>>.Success([new(_account,"Liable holder","Space 4","Kanmanggay",false)]));
        var cut=RenderComponent<FineAssessmentDrawer>(); cut.Find("input").Change("Liable"); cut.FindAll("button").First(x=>x.TextContent.Trim()=="Search").Click();
        cut.WaitForAssertion(()=>Assert.Contains("Liable holder",cut.Markup)); cut.Find(".fine-result").Click();
        Assert.Contains("No approved fine policy is configured for this source",cut.Find(".fine-empty").TextContent);
        Assert.Contains("Configure penalty settings",cut.Find(".fine-empty").TextContent);
        Assert.Empty(cut.FindAll("select"));
    }
    [Fact] public void DirectFineReceiptParticularsAreConditional()
    {
        DirectCategory(true); var cut=RenderComponent<DirectFineCollectionDrawer>();
        Assert.Contains("Payer name / receipt particulars",cut.Markup); Assert.Contains("Supporting reference",cut.Markup);
        Assert.DoesNotContain("receipt particulars (optional)",cut.Markup); Assert.DoesNotContain("Supporting reference (optional)",cut.Markup);
        Assert.Equal(4,cut.FindAll("input[required], textarea[required]").Count);
        ExportFixture("direct-fine-required-evidence",cut.Markup);
    }
    [Fact] public void DirectFineUnavailableAuthorityAndReadFailureRemainDistinct()
    {
        _api.Setup(x=>x.GetDirectFineOptionsAsync()).ReturnsAsync(Result<DirectFineOptionsDto>.Success(new(new(2026,10,11),[])));
        var cut=RenderComponent<DirectFineCollectionDrawer>(); Assert.Contains("No approved direct-fine collection policy",cut.Markup);
        Assert.NotNull(cut.Find("button[type='submit']").GetAttribute("disabled")); ExportFixture("direct-fine-policy-empty",cut.Markup);
        _api.Setup(x=>x.GetDirectFineOptionsAsync()).ReturnsAsync(Result<DirectFineOptionsDto>.Failure("Unavailable"));
        var failed=RenderComponent<DirectFineCollectionDrawer>(); Assert.Contains("Unavailable",failed.Markup); Assert.DoesNotContain("No approved",failed.Markup);
        ExportFixture("direct-fine-policy-error",failed.Markup);
    }
    [Fact] public void DirectFineReviewRequiresActualMoneyBeforeCashPost()
    {
        DirectCategory(); var cut=RenderComponent<DirectFineCollectionDrawer>(); cut.Find("input[type='number']").Change("200"); cut.Find("form").Submit();
        Assert.Contains("Oct 11, 2026",cut.Markup); Assert.Contains("₱200.00",cut.Markup); ExportFixture("direct-fine-review",cut.Markup);
        cut.Find("form").Submit(); Assert.Contains("Confirm that money",cut.Markup);
        _api.Verify(x=>x.PostDirectFineAsync(It.IsAny<DirectFineCollectionRequest>()),Times.Never);
        _api.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
    }
    [Fact] public void DirectFineSuccessShowsRealSrcWithoutCreatingAssessment()
    {
        var c=DirectCategory(); var refreshed=0;
        _api.Setup(x=>x.PostDirectFineAsync(It.IsAny<DirectFineCollectionRequest>())).ReturnsAsync(Result<GovernedServiceOutcomeDto>.Success(new(Guid.NewGuid(),"SRC-TEST-ACTUAL",new(2026,10,11),200m,RevenueInstrumentType.OfficialReceipt,"Posted",false)));
        var cut=RenderComponent<DirectFineCollectionDrawer>(p=>p.Add(x=>x.OnCollected,()=>refreshed++));
        cut.Find("input[type='number']").Change("200"); cut.Find("form").Submit(); cut.Find("input[type='checkbox']").Change(true); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Contains("SRC-TEST-ACTUAL",cut.Markup)); Assert.Equal(1,refreshed);
        Assert.NotNull(cut.Find("button[type='submit']").GetAttribute("disabled"));
        _api.Verify(x=>x.PostDirectFineAsync(It.Is<DirectFineCollectionRequest>(r=>r.AuthorityId==c.AuthorityId && r.Amount==200m && r.PayerParticulars==null)),Times.Once);
        _api.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
        ExportFixture("direct-fine-success",cut.Markup);
    }
    [Fact] public void DirectFineUnknownResultRetriesSameIntentWithoutEditableDetails()
    {
        DirectCategory(); var requests=new List<DirectFineCollectionRequest>();
        _api.Setup(x=>x.PostDirectFineAsync(It.IsAny<DirectFineCollectionRequest>())).Callback<DirectFineCollectionRequest>(requests.Add).ReturnsAsync(Result<GovernedServiceOutcomeDto>.Failure("Unknown response"));
        var cut=RenderComponent<DirectFineCollectionDrawer>(); cut.Find("input[type='number']").Change("200"); cut.Find("form").Submit(); cut.Find("input[type='checkbox']").Change(true);
        cut.Find("form").Submit(); Assert.DoesNotContain("Back to details",cut.Markup); cut.Find("form").Submit();
        Assert.Equal(2,requests.Count); Assert.Equal(requests[0],requests[1]);
        ExportFixture("direct-fine-post-retry",cut.Markup);
    }
    [Fact] public void CollectionsDefaultAndAdminCannotOpenDirectWriter()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        _api.Setup(x=>x.GetRegisterAsync(It.IsAny<DateOnly>(),It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<PenaltyRegisterRowDto>>.Success([]));
        var cut=RenderComponent<Fines>(); Assert.Empty(cut.FindComponents<FineAssessmentsRegister>()); Assert.DoesNotContain("Record direct fine",cut.Markup); Assert.DoesNotContain(cut.FindAll("button"),b=>b.TextContent=="Assess fine");
        var first=cut.Find("[aria-label='Fines workspace views'] button"); Assert.Equal("Collections",first.TextContent); Assert.Equal("true",first.GetAttribute("aria-pressed"));
    }
    [Fact] public void HeadDirectActionNeverOpensAssessmentSelector()
    {
        DirectCategory(); _api.Setup(x=>x.GetRegisterAsync(It.IsAny<DateOnly>(),It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<PenaltyRegisterRowDto>>.Success([]));
        var cut=RenderComponent<Fines>(); cut.FindAll("button").Single(x=>x.TextContent=="Record direct fine").Click();
        Assert.Single(cut.FindComponents<DirectFineCollectionDrawer>()); Assert.Empty(cut.FindComponents<FineAssessmentDrawer>()); Assert.Empty(cut.FindComponents<FinePaymentDrawer>());
    }
}
