using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;
public sealed partial class FineAssessmentComponentTests : TestContext
{
    private readonly Mock<IPenaltiesApiClient> _api=new();
    private readonly FineAccountRef _account=new(FineAccountKind.Kanmanggay,Guid.NewGuid());
    public FineAssessmentComponentTests() {
        Services.AddSingleton(_api.Object); Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>()); Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>()); Services.AddSingleton<BrandingState>();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin"); JSInterop.Mode=JSRuntimeMode.Loose;
        _api.Setup(x=>x.GetFinePoliciesAsync()).ReturnsAsync(Result<IReadOnlyList<FinePolicyDto>>.Success([]));
        _api.Setup(x=>x.GetDefinitionsAsync()).ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Success([]));
        _api.Setup(x=>x.GetFinePeriodsAsync(It.IsAny<FineAccountRef>(),It.IsAny<int?>())).ReturnsAsync(Result<IReadOnlyList<FinePeriodOption>>.Success([]));
        _api.Setup(x=>x.GetFineAccountAsync(It.IsAny<FineAccountRef>())).ReturnsAsync(Result<FineAccountOption>.Success(new(_account,"Liable holder","Space 4","Kanmanggay",false)));
        _api.Setup(x=>x.GetAssessmentsAsync(It.IsAny<FineAccountKind?>(),It.IsAny<Guid?>(),It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([]));
    }
    private FineAssessmentDto Row() => new(Guid.NewGuid(),_account,"Liable holder","Space 4","Kanmanggay","Approved violation",
        "Recorded incident",new(2026,9,1),new(2026,10,10),"CASE-1",120m,0m,120m,"Outstanding",1,"AUTHORITY-1",DateTime.UtcNow,null);
    private FinePolicyDto Policy() {
        var definition=new PenaltyDefinitionDto(Guid.NewGuid(),"APPROVED","Approved violation",null,GovernedServiceBasis.DirectApprovedAmount,null,200m,true,new(2026,1,1));
        var policy=new FinePolicyDto(Guid.NewGuid(),new(definition.VersionId,FineAccountKind.Kanmanggay,null,"AUTHORITY-1",FinePenaltyDateBasis.AssessmentDate,false,false),definition);
        _api.Setup(x=>x.GetDefinitionsAsync()).ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Success([definition]));
        _api.Setup(x=>x.GetFinePoliciesAsync()).ReturnsAsync(Result<IReadOnlyList<FinePolicyDto>>.Success([policy])); return policy;
    }
    [Fact] public void RegisterSeparatesOutstandingFromCash() {
        _api.Setup(x=>x.GetAssessmentsAsync(null,null,It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([Row()]));
        var cut=RenderComponent<FineAssessmentsRegister>();
        cut.WaitForAssertion(()=>Assert.Contains("Recorded incident",cut.Markup));
        Assert.Contains("Outstanding",cut.Markup); Assert.Contains("Record payment",cut.Markup);
        Assert.Equal(8,cut.FindAll("th").Count); Assert.Contains("View details",cut.Markup);
        ExportFixture("fine-assessments",cut.Markup);
    }
    [Fact] public void AdminHasNoAssessmentCreateAction() {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        var cut=RenderComponent<FineAssessmentsRegister>(); Assert.DoesNotContain("Assess fine</button>",cut.Markup);
    }
    [Fact] public void FailedReadDoesNotClaimNoAssessmentsOrZeroBalance() {
        _api.Setup(x=>x.GetAssessmentsAsync(It.IsAny<FineAccountKind?>(),It.IsAny<Guid?>(),It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Failure("Unavailable"));
        var cut=RenderComponent<AccountFineAssessments>(p=>p.Add(x=>x.Account,_account));
        cut.WaitForAssertion(()=>Assert.Contains("Fine assessments are unavailable",cut.Markup));
        Assert.DoesNotContain("No saved fine assessments",cut.Markup); Assert.Empty(cut.FindAll("table"));
    }
    [Fact] public void AccountChargesUseExactAccountReadAndKeepRentalTotalsSeparate() {
        _api.Setup(x=>x.GetAssessmentsAsync(_account.Kind,_account.AccountId,null)).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([Row()]));
        var cut=RenderComponent<AccountFineAssessments>(p=>p.Add(x=>x.Account,_account));
        cut.WaitForAssertion(()=>Assert.Contains("₱120.00",cut.Markup));
        _api.Verify(x=>x.GetAssessmentsAsync(_account.Kind,_account.AccountId,null),Times.Once);
        Assert.DoesNotContain("Total outstanding rental",cut.Markup);
        ExportFixture("fine-account-charges",cut.Markup);
    }
    [Fact] public void WrongAccountRowsAreNotDisplayed() {
        _api.Setup(x=>x.GetAssessmentsAsync(It.IsAny<FineAccountKind?>(),It.IsAny<Guid?>(),It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([Row() with {Account=new(FineAccountKind.Kanmanggay,Guid.NewGuid())}]));
        var cut=RenderComponent<AccountFineAssessments>(p=>p.Add(x=>x.Account,_account));
        cut.WaitForAssertion(()=>Assert.Contains("different account",cut.Markup)); Assert.Empty(cut.FindAll("table"));
    }
    [Fact] public void HeadAssessLinkNeedsSourcePolicy() {
        var cut=RenderComponent<AccountFineAssessments>(p=>p.Add(x=>x.Account,_account)); Assert.Empty(cut.FindAll("a"));
        Policy(); cut.SetParametersAndRender(p=>p.Add(x=>x.Account,_account).Add(x=>x.RefreshVersion,1));
        cut.WaitForAssertion(()=>Assert.Contains($"accountId={_account.AccountId}",cut.Markup));
    }
    [Fact] public void DrawerShowsAmountConstraintsAndSavesOnlyAfterReview() {
        var policy=Policy(); var saved=0;
        _api.Setup(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>())).ReturnsAsync(Result<FineAssessmentDto>.Success(Row()));
        var cut=RenderComponent<FineAssessmentDrawer>(p=>p.Add(x=>x.Account,_account).Add(x=>x.OnSaved,()=>saved++));
        cut.WaitForAssertion(()=>Assert.Contains("Liable holder",cut.Markup));
        cut.Find("select").Change(policy.Id.ToString()); ChooseOther(cut,"Actual incident"); cut.Find("input[type='number']").Change("120");
        Assert.Contains("Maximum ₱200.00",cut.Markup); ExportFixture("fine-drawer",cut.Markup); cut.Find("form").Submit();
        _api.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
        Assert.Contains("Confirm assessment",cut.Markup); ExportFixture("fine-review",cut.Markup); cut.Find("form").Submit();
        cut.WaitForAssertion(()=>Assert.Equal(1,saved));
        _api.Verify(x=>x.CreateAssessmentAsync(It.Is<CreateFineAssessmentRequest>(r=>r.Account==_account&&r.Amount==120m&&r.PolicyId==policy.Id&&r.Description=="Actual incident")),Times.Once);
    }
    [Fact] public void DrawerCancelNeverCreatesLiability() {
        var closed=0; var cut=RenderComponent<FineAssessmentDrawer>(p=>p.Add(x=>x.OnClose,()=>closed++));
        cut.FindAll("button").Single(b=>b.TextContent.Trim()=="Cancel").Click(); Assert.Equal(1,closed);
        _api.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
    }
    [Fact] public void MissingPolicyCannotSubmitFakeFine() {
        var cut=RenderComponent<FineAssessmentDrawer>(p=>p.Add(x=>x.Account,_account));
        cut.WaitForAssertion(()=>Assert.Contains("Liable holder",cut.Markup)); cut.Find("form").Submit();
        Assert.Contains("Select an applicable approved fine category",cut.Markup); _api.Verify(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>()),Times.Never);
    }
    private static void ExportFixture(string name,string markup) {
        var directory=Environment.GetEnvironmentVariable("STALLTRACK_FINE_FIXTURE_DIR");
        if(string.IsNullOrEmpty(directory)) return;
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../.."));
        var links=new[]{"EEMOCantilanSDS.Client/wwwroot/css/site.min.css","EEMOCantilanSDS.Client/wwwroot/app.css",
            "EEMOCantilanSDS.Client/obj/Release/net10.0/scopedcss/bundle/EEMOCantilanSDS.Client.styles.css"}
            .Select(path=>$"<link rel='stylesheet' href='{new Uri(Path.Combine(root,path)).AbsoluteUri}'>");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,name+".html"),"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>"+
            string.Join("",links)+"</head><body><main class='v3-page'>"+markup+"</main></body></html>",new System.Text.UTF8Encoding(false));
    }
}
