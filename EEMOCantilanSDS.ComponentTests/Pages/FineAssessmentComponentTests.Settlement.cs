using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;
public sealed partial class FineAssessmentComponentTests
{
    private FineSettlementQuoteDto PaymentQuote(Guid id, bool payable = true) => new(id, _account,
        "Liable holder", "Kanmanggay", "Space 4", 120m, payable ? 0m : 120m, payable ? 120m : 0m,
        payable ? "Outstanding" : "Settled", 1, "server-quote", RevenueInstrumentType.OfficialReceipt,
        payable, payable ? null : "The fine is already settled.");
    private static EcfCollectionDraftDto PaymentDraft(Guid? assessmentId = null) => new(Guid.NewGuid(),1,new(2026,10,10),"Open",false,null,
        "Liable holder",null,null,null,120m,null,[new(Guid.NewGuid(),120m,"Fines",Guid.Empty,default,1,120m,CollectionSourceKind.FineAssessment,assessmentId)]);
    private void Quote(Guid id, bool payable = true) => _api.Setup(a=>a.QuoteFineAsync(id))
        .ReturnsAsync(Result<FineSettlementQuoteDto>.Success(PaymentQuote(id,payable)));

    [Fact] public void FinePaymentRequiresReviewAndPostsOnlyTheQuotedAssessment()
    {
        var id=Guid.NewGuid(); Quote(id); var draft=PaymentDraft(id); var paid=0;
        _api.Setup(a=>a.AddFineDraftAsync(It.IsAny<AddFineAssessmentDraftRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(draft));
        _api.Setup(a=>a.ReviewFineDraftAsync(draft.DraftId,It.IsAny<EcfDraftRevisionRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(draft with {IsReviewedForCurrentRevision=true}));
        _api.Setup(a=>a.PostFineDraftAsync(draft.DraftId,It.IsAny<PostEcfCollectionDraftRequest>()))
            .ReturnsAsync(Result<EcfPostOutcomeDto>.Success(new(Guid.NewGuid(),"SRC-TEST","Posted",120m,1,false)));
        var cut=RenderComponent<FinePaymentDrawer>(p=>p.Add(c=>c.AssessmentId,id).Add(c=>c.OnPaid,()=>paid++));
        cut.WaitForAssertion(()=>Assert.Contains("₱120.00",cut.Markup)); Assert.Empty(cut.FindAll("input[type=number]"));
        ExportFixture("fine-payment",cut.Markup); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Contains("Record payment",cut.Markup));
        _api.Verify(a=>a.PostFineDraftAsync(It.IsAny<Guid>(),It.IsAny<PostEcfCollectionDraftRequest>()),Times.Never);
        ExportFixture("fine-payment-review",cut.Markup); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Equal(1,paid)); Assert.Contains("SRC-TEST",cut.Markup);
        cut.Find("form").Submit();
        _api.Verify(a=>a.AddFineDraftAsync(It.Is<AddFineAssessmentDraftRequest>(r=>r.AssessmentId==id&&r.Amount==120m&&r.QuoteToken=="server-quote")),Times.Once);
        _api.Verify(a=>a.PostFineDraftAsync(draft.DraftId,It.IsAny<PostEcfCollectionDraftRequest>()),Times.Once);
        _api.Verify(a=>a.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
    }
    [Fact] public void UnavailableFineQuoteDoesNotDisplayAFabricatedZeroOrPost()
    {
        var id=Guid.NewGuid(); _api.Setup(a=>a.QuoteFineAsync(id)).ReturnsAsync(Result<FineSettlementQuoteDto>.Failure("Permission denied"));
        var cut=RenderComponent<FinePaymentDrawer>(p=>p.Add(c=>c.AssessmentId,id));
        cut.WaitForAssertion(()=>Assert.Contains("Permission denied",cut.Markup)); Assert.DoesNotContain("₱0.00",cut.Markup);
        cut.Find("form").Submit(); _api.Verify(a=>a.AddFineDraftAsync(It.IsAny<AddFineAssessmentDraftRequest>()),Times.Never);
    }
    [Fact] public void AlreadySettledFineCannotCreateAnotherPayment()
    {
        var id=Guid.NewGuid(); Quote(id,false); var cut=RenderComponent<FinePaymentDrawer>(p=>p.Add(c=>c.AssessmentId,id));
        cut.WaitForAssertion(()=>Assert.Contains("already settled",cut.Markup)); cut.Find("form").Submit();
        _api.Verify(a=>a.AddFineDraftAsync(It.IsAny<AddFineAssessmentDraftRequest>()),Times.Never);
    }
    [Fact] public void FailedReviewRetriesTheSameSavedDraftWithoutAddingAnotherCharge()
    {
        var id=Guid.NewGuid(); Quote(id); var draft=PaymentDraft(id);
        _api.Setup(a=>a.AddFineDraftAsync(It.IsAny<AddFineAssessmentDraftRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(draft));
        _api.SetupSequence(a=>a.ReviewFineDraftAsync(draft.DraftId,It.IsAny<EcfDraftRevisionRequest>()))
            .ReturnsAsync(Result<EcfCollectionDraftDto>.Failure("Reload review"))
            .ReturnsAsync(Result<EcfCollectionDraftDto>.Success(draft with {IsReviewedForCurrentRevision=true}));
        var cut=RenderComponent<FinePaymentDrawer>(p=>p.Add(c=>c.AssessmentId,id)); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Contains("Reload review",cut.Markup)); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Contains("Record payment",cut.Markup));
        _api.Verify(a=>a.AddFineDraftAsync(It.IsAny<AddFineAssessmentDraftRequest>()),Times.Once);
        _api.Verify(a=>a.PostFineDraftAsync(It.IsAny<Guid>(),It.IsAny<PostEcfCollectionDraftRequest>()),Times.Never);
    }
    [Fact] public void LostPostResponseRetainsTheSameDurableOperationId()
    {
        var id=Guid.NewGuid(); Quote(id); var draft=PaymentDraft(id); var operations=new List<Guid>();
        _api.Setup(a=>a.AddFineDraftAsync(It.IsAny<AddFineAssessmentDraftRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(draft));
        _api.Setup(a=>a.ReviewFineDraftAsync(draft.DraftId,It.IsAny<EcfDraftRevisionRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(draft));
        _api.Setup(a=>a.PostFineDraftAsync(draft.DraftId,It.IsAny<PostEcfCollectionDraftRequest>()))
            .Callback<Guid,PostEcfCollectionDraftRequest>((_,r)=>operations.Add(r.ClientOperationId))
            .ThrowsAsync(new IOException("Lost response"));
        var cut=RenderComponent<FinePaymentDrawer>(p=>p.Add(c=>c.AssessmentId,id)); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Contains("Record payment",cut.Markup)); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Single(operations)); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Equal(2,operations.Count)); Assert.Equal(operations[0],operations[1]); Assert.NotEqual(Guid.Empty,operations[0]);
    }
    [Fact] public void CompatibleRentalDraftIsVisibleAndUsesItsExpectedRevision()
    {
        var id=Guid.NewGuid(); Quote(id);
        var rent=new EcfCollectionDraftLineDto(Guid.NewGuid(),900m,"Space Rental",Guid.Empty,default,1,900m);
        var current=PaymentDraft() with {Revision=4,TotalAmount=900m,Lines=[rent]};
        var combined=current with {Revision=5,TotalAmount=1020m,Lines=[rent,new(Guid.NewGuid(),120m,"Fines",Guid.Empty,default,1,120m,CollectionSourceKind.FineAssessment,id)]};
        _api.Setup(a=>a.GetCurrentFineDraftAsync(id)).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(current));
        _api.Setup(a=>a.AddFineDraftAsync(It.IsAny<AddFineAssessmentDraftRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(combined));
        _api.Setup(a=>a.ReviewFineDraftAsync(current.DraftId,It.IsAny<EcfDraftRevisionRequest>())).ReturnsAsync(Result<EcfCollectionDraftDto>.Success(combined));
        var cut=RenderComponent<FinePaymentDrawer>(p=>p.Add(c=>c.AssessmentId,id));
        cut.WaitForAssertion(()=>Assert.Contains("Space Rental",cut.Markup)); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Contains("₱1,020.00",cut.Markup)); Assert.Contains("Fines",cut.Markup);
        _api.Verify(a=>a.AddFineDraftAsync(It.Is<AddFineAssessmentDraftRequest>(r=>r.ExpectedRevision==4&&r.AssessmentId==id&&r.Amount==120m)),Times.Once);
        ExportFixture("fine-payment-mixed-review",cut.Markup);
    }
    [Fact] public void AssessmentDetailsShowActualSrcAllocationAndCorrectionEffect()
    {
        var row=Row();
        _api.Setup(a=>a.GetAssessmentsAsync(null,null,It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([row]));
        _api.Setup(a=>a.GetAssessmentHistoryAsync(row.Id)).ReturnsAsync(Result<IReadOnlyList<FineAssessmentEventDto>>.Success([]));
        _api.Setup(a=>a.GetFineSettlementHistoryAsync(row.Id)).ReturnsAsync(Result<IReadOnlyList<FineSettlementHistoryDto>>.Success([new(Guid.NewGuid(),"SRC-RECORDED",new(2026,10,10),120m,0m)]));
        var cut=RenderComponent<FineAssessmentsRegister>(); cut.FindAll("button").Single(b=>b.TextContent=="View details").Click();
        cut.WaitForAssertion(()=>Assert.Contains("SRC-RECORDED",cut.Markup)); Assert.Contains("Effective",cut.Markup); Assert.Contains("₱0.00",cut.Markup);
        _api.Verify(a=>a.GetFineSettlementHistoryAsync(row.Id),Times.Once);
    }
}
