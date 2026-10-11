using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
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
        Assert.DoesNotContain("Payer / receipt particulars",cut.Markup); Assert.DoesNotContain("Contract",cut.Markup);
        ExportFixture("direct-fine-form",cut.Markup);
    }
    [Fact] public void DirectFineReceiptParticularsAreConditional()
    {
        DirectCategory(true); var cut=RenderComponent<DirectFineCollectionDrawer>();
        Assert.Contains("Payer / receipt particulars",cut.Markup); Assert.Contains("Supporting reference",cut.Markup);
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
        var cut=RenderComponent<Fines>(); Assert.Empty(cut.FindComponents<FineAssessmentsRegister>()); Assert.DoesNotContain("Record fine collection",cut.Markup);
        var first=cut.Find("[aria-label='Fines workspace views'] button"); Assert.Equal("Collections",first.TextContent); Assert.Equal("true",first.GetAttribute("aria-pressed"));
    }
    [Fact] public void HeadDirectActionNeverOpensAssessmentSelector()
    {
        DirectCategory(); _api.Setup(x=>x.GetRegisterAsync(It.IsAny<DateOnly>(),It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<PenaltyRegisterRowDto>>.Success([]));
        var cut=RenderComponent<Fines>(); cut.FindAll("button").Single(x=>x.TextContent=="Record fine collection").Click();
        Assert.Single(cut.FindComponents<DirectFineCollectionDrawer>()); Assert.Empty(cut.FindComponents<FineAssessmentDrawer>()); Assert.Empty(cut.FindComponents<FinePaymentDrawer>());
    }
}
