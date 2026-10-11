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
    [Fact] public void NoAccountShowsSelectionGuidanceNotPolicyFailure()
    {
        var cut=RenderComponent<FineAssessmentDrawer>();
        Assert.Contains("Select an account to view applicable fines",cut.Markup);
        Assert.DoesNotContain("No approved fine policy",cut.Markup);
        Assert.Empty(cut.FindAll("select"));
        ExportFixture("fine-no-account",cut.Markup);
    }
    [Fact] public void NoPolicyHasSeparateHeadSettingsPathAndNoMonetaryInput()
    {
        var cut=RenderComponent<FineAssessmentDrawer>(p=>p.Add(x=>x.Account,_account));
        Assert.Contains("No approved fine policy is configured for this source",cut.Markup);
        Assert.Equal("/operations/fines?view=settings",cut.Find("a").GetAttribute("href"));
        Assert.Empty(cut.FindAll("input[type=number]"));
        ExportFixture("fine-no-policy",cut.Markup);
    }
    [Fact] public void VerifiedPeriodPersistsThroughReviewBackAndAuthoritativeSave()
    {
        var policy=Policy();var period=new FinePeriodOption(new(FineAffectedPeriodKind.ObligationPeriod,new(2026,9,1),new(2026,9,1),Guid.NewGuid()),"September 2026");
        _api.Setup(x=>x.GetFinePeriodsAsync(_account,null)).ReturnsAsync(Result<IReadOnlyList<FinePeriodOption>>.Success([period]));
        _api.Setup(x=>x.CreateAssessmentAsync(It.IsAny<CreateFineAssessmentRequest>())).ReturnsAsync(Result<FineAssessmentDto>.Success(Row() with {RelatedPeriod=period}));
        var cut=RenderComponent<FineAssessmentDrawer>(p=>p.Add(x=>x.Account,_account));
        cut.Find("select").Change(policy.Id.ToString());cut.Find("select[aria-label='Related billing period']").Change("0");
        cut.Find("textarea").Change("Recorded offense");cut.Find("input[aria-label='Fine amount']").Change("120");
        ExportFixture("fine-period-details",cut.Markup);cut.Find("form").Submit();
        Assert.Contains("September 2026",cut.Markup);ExportFixture("fine-period-review",cut.Markup);
        cut.FindAll("footer button").Single(x=>x.TextContent.Trim()=="Back").Click();
        Assert.Equal("Recorded offense",cut.Find("textarea").GetAttribute("value"));
        Assert.Equal("0",cut.Find("select[aria-label='Related billing period']").GetAttribute("value") ?? cut.Find("select[aria-label='Related billing period']").QuerySelector("option[selected]")?.GetAttribute("value"));
        cut.Find("form").Submit();cut.Find("form").Submit();
        _api.Verify(x=>x.CreateAssessmentAsync(It.Is<CreateFineAssessmentRequest>(r=>r.RelatedPeriod==period.Reference&&r.Account==_account)),Times.Once);
    }
    [Fact] public void FixedAmountUsesApprovedValueAndLocksInput()
    {
        var policy=Policy();var definition=policy.Definition! with {Basis=GovernedServiceBasis.FixedAmount,FixedAmount=75,MaximumAmount=null};
        _api.Setup(x=>x.GetDefinitionsAsync()).ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Success([definition]));
        var cut=RenderComponent<FineAssessmentDrawer>(p=>p.Add(x=>x.Account,_account));cut.Find("select").Change(policy.Id.ToString());
        var input=cut.Find("input[aria-label='Fine amount']");Assert.True(input.HasAttribute("readonly"));Assert.Equal("75",input.GetAttribute("value"));
    }
    [Fact] public void PeriodReadFailureDoesNotInventPeriodsOrPreventGeneralOffense()
    {
        var policy=Policy();_api.Setup(x=>x.GetFinePeriodsAsync(_account,null)).ReturnsAsync(Result<IReadOnlyList<FinePeriodOption>>.Failure("offline"));
        var cut=RenderComponent<FineAssessmentDrawer>(p=>p.Add(x=>x.Account,_account));
        Assert.Single(cut.FindAll("select[aria-label='Related billing period'] option"));Assert.Contains("Related period evidence is unavailable",cut.Markup);
        cut.Find("select").Change(policy.Id.ToString());cut.Find("textarea").Change("Contract offense");cut.Find("input[aria-label='Fine amount']").Change("120");cut.Find("form").Submit();
        Assert.Contains("Not related to a billing period",cut.Markup);
    }
    [Fact] public void ExistingAssessmentsStayVisibleWhenNewPolicyIsUnavailable()
    {
        var period=new FinePeriodOption(new(FineAffectedPeriodKind.ObligationPeriod,new(2026,9,1),new(2026,9,1),Guid.NewGuid()),"September 2026");
        _api.Setup(x=>x.GetAssessmentsAsync(_account.Kind,_account.AccountId,null)).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success([Row() with {RelatedPeriod=period}]));
        var cut=RenderComponent<AccountFineAssessments>(p=>p.Add(x=>x.Account,_account));
        Assert.Contains("September 2026",cut.Markup);Assert.Contains("₱120.00",cut.Markup);Assert.Contains("No approved fine policy",cut.Markup);
    }
    [Fact] public void PenaltySettingsUseVerifiedReadableSourcesAndConditionalAuthority()
    {
        var policy=Policy();var definition=policy.Definition!;
        _api.Setup(x=>x.GetFineSourcesAsync()).ReturnsAsync(Result<IReadOnlyList<FineSourceOption>>.Success([new("Tenant market",FineAccountKind.FacilityContract,FacilityCode.NPM)]));
        var cut=RenderComponent<FinePolicySetup>(p=>p.Add(x=>x.Definitions,new[]{definition}));
        cut.Find("button").Click();cut.Find("select").Change(definition.VersionId.ToString());
        Assert.Contains("Tenant market",cut.Markup);Assert.DoesNotContain(">FacilityContract<",cut.Markup);
        Assert.DoesNotContain("Uncapped amount authorization",cut.Markup);Assert.Contains("Closed-account assessment authorization",cut.Markup);
        ExportFixture("fine-policy-details",cut.Markup);
        var saved=false;
        var recorded=policy with {Evidence=policy.Evidence with {AccountKind=FineAccountKind.FacilityContract,Facility=FacilityCode.NPM,GoverningReference="TEST approved reference"}};
        _api.Setup(x=>x.RecordFinePolicyAsync(It.IsAny<FinePolicyRequest>())).Callback(()=>saved=true).ReturnsAsync(Result<FinePolicyDto>.Success(recorded));
        _api.Setup(x=>x.GetFinePoliciesAsync()).ReturnsAsync(()=>Result<IReadOnlyList<FinePolicyDto>>.Success(saved ? [recorded] : []));
        cut.Find("input[placeholder='Approved ordinance, schedule or authority']").Change("TEST approved reference");
        cut.Find("form").Submit();ExportFixture("fine-policy-review",cut.Markup);
        Assert.Equal(2,cut.FindAll("footer button").Count);
        cut.Find("form").Submit();Assert.False(saved);
        cut.Find("input[type=checkbox]").Change(true);cut.Find("form").Submit();
        Assert.True(saved);Assert.Empty(cut.FindAll("[role=dialog]"));Assert.Contains("TEST approved reference",cut.Markup);
        _api.Verify(x=>x.RecordFinePolicyAsync(It.Is<FinePolicyRequest>(r=>r.AccountKind==FineAccountKind.FacilityContract&&r.Facility==FacilityCode.NPM&&!r.AllowsUncappedAmount&&r.GoverningReference=="TEST approved reference")),Times.Once);
    }
    [Fact] public void AdminSettingsReadHasNoWriteAction()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        _api.Setup(x=>x.GetFineSourcesAsync()).ReturnsAsync(Result<IReadOnlyList<FineSourceOption>>.Success([]));
        var cut=RenderComponent<FinePolicySetup>();Assert.Contains("Where fines apply",cut.Markup);Assert.Empty(cut.FindAll("button"));
    }
    [Fact] public void WorkspaceShowsOneViewAndKeepsDeepLinkedSettings()
    {
        _api.Setup(x=>x.GetRegisterAsync(It.IsAny<DateOnly>(),It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<PenaltyRegisterRowDto>>.Success([]));
        _api.Setup(x=>x.GetFineSourcesAsync()).ReturnsAsync(Result<IReadOnlyList<FineSourceOption>>.Success([]));
        var cut=RenderComponent<Fines>();Assert.Empty(cut.FindComponents<FineAssessmentsRegister>());Assert.Contains("Record fine collection",cut.Markup);Assert.DoesNotContain("id=\"fn-defs-title\"",cut.Markup);
        ExportFixture("fine-workspace-collected",cut.Markup);
        cut.FindAll("[aria-label='Fines workspace views'] button").Single(x=>x.TextContent=="Assessments").Click();Assert.Single(cut.FindComponents<FineAssessmentsRegister>());
        ExportFixture("fine-workspace-assessments",cut.Markup);
        cut.FindAll("[aria-label='Fines workspace views'] button").Single(x=>x.TextContent=="Collections").Click();Assert.Empty(cut.FindComponents<FineAssessmentsRegister>());Assert.Contains("Collections",cut.Markup);
        ExportFixture("fine-workspace-collected",cut.Markup);
        cut.FindAll("[aria-label='Fines workspace views'] button").Single(x=>x.TextContent=="Penalty settings").Click();Assert.Single(cut.FindComponents<FinePolicySetup>());Assert.DoesNotContain("id=\"fn-register-title\"",cut.Markup);
        ExportFixture("fine-workspace-settings",cut.Markup);
    }
}
