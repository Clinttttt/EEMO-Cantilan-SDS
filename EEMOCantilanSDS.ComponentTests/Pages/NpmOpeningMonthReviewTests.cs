using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Client.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class NpmOpeningMonthReviewTests : TestContext
{
    private readonly Guid stall = Guid.NewGuid(), contract = Guid.NewGuid();
    private readonly DateOnly period = new(2026, 10, 1);
    private readonly Mock<IDailyCollectionApiClient> api = new();
    private NpmOpeningMonthAssessmentReview Review(Guid? month = null, bool active = false) => new(month,
        month is null ? null : active ? 3 : 1, 900, 0, new("ApprovedFullOpeningMonthV1", Guid.Empty, Guid.Empty,
            stall, contract, new(2026, 10, 20), new(2026, 10, 20), Guid.Empty, new(2026, 10, 20), 900, 30,
            4321, 4567, "Synthetic Head", DateTime.UnixEpoch, Guid.Empty, DateTime.UnixEpoch,
            new(2026, 10, 20), "TEST-APPROVAL", true, false), active);
    private IRenderedComponent<NpmOpeningMonthReview> Render(bool enabled = true)
    {
        var auth = this.AddTestAuthorization(); auth.SetAuthorized("Synthetic Head"); auth.SetRoles("SuperAdmin");
        Services.AddSingleton(api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(new EEMOCantilanSDS.Client.Services.BrandingState(Mock.Of<IMunicipalitiesApiClient>()));
        api.Setup(x => x.IsNpmMonthlySettlementAvailableAsync()).ReturnsAsync(Result<bool>.Success(enabled));
        return RenderComponent<NpmOpeningMonthReview>(p => p.Add(x => x.StallId, stall).Add(x => x.ContractId, contract)
            .Add(x => x.PeriodStart, period).Add(x => x.AutoOpen, true));
    }
    private static void Confirm(IRenderedComponent<NpmOpeningMonthReview> cut)
    {
        cut.Find("input[maxlength]").Change("TEST-HEAD-DECISION");
        cut.Find("input[type=checkbox]").Change(true);
        cut.Find("button.btn-primary").Click();
    }
    [Fact]
    public void Disabled_policy_exposes_no_drawer_or_assessment_request()
    {
        var cut = Render(false);
        Assert.Empty(cut.FindAll("button"));
        api.Verify(x => x.ReviewNpmOpeningMonthAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>()), Times.Never);
    }
    [Fact]
    public void Head_reviews_server_amount_then_prepares_and_authorizes_one_month()
    {
        var month = Guid.NewGuid();
        api.SetupSequence(x => x.ReviewNpmOpeningMonthAsync(stall, contract, period))
            .ReturnsAsync(Result<NpmOpeningMonthAssessmentReview>.Success(Review()))
            .ReturnsAsync(Result<NpmOpeningMonthAssessmentReview>.Success(Review()))
            .ReturnsAsync(Result<NpmOpeningMonthAssessmentReview>.Success(Review(month)))
            .ReturnsAsync(Result<NpmOpeningMonthAssessmentReview>.Success(Review(month, true)));
        api.Setup(x => x.PrepareNpmOpeningMonthAsync(new(stall, contract, period))).ReturnsAsync(Result<Guid>.Success(month));
        api.Setup(x => x.ActivateNpmOpeningMonthAsync(new(stall, contract, period, 1, "TEST-HEAD-DECISION"))).ReturnsAsync(Result<long>.Success(3));
        var cut = Render();
        Assert.Contains("900.00", cut.Markup);
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));
        Confirm(cut);
        cut.WaitForAssertion(() => Assert.Contains("Monthly rent is activated", cut.Markup));
        api.Verify(x => x.PrepareNpmOpeningMonthAsync(It.IsAny<NpmOpeningMonthPreparationRequest>()), Times.Once);
        api.Verify(x => x.ActivateNpmOpeningMonthAsync(It.IsAny<NpmOpeningMonthActivationRequest>()), Times.Once);
    }
    [Fact]
    public void Changed_rate_requires_another_explicit_review_before_preparation()
    {
        api.SetupSequence(x => x.ReviewNpmOpeningMonthAsync(stall, contract, period))
            .ReturnsAsync(Result<NpmOpeningMonthAssessmentReview>.Success(Review()))
            .ReturnsAsync(Result<NpmOpeningMonthAssessmentReview>.Success(Review() with { GrossObligation = 1000 }));
        var cut = Render(); Confirm(cut);
        cut.WaitForAssertion(() => Assert.Contains("assessment changed", cut.Markup));
        api.Verify(x => x.PrepareNpmOpeningMonthAsync(It.IsAny<NpmOpeningMonthPreparationRequest>()), Times.Never);
        api.Verify(x => x.ActivateNpmOpeningMonthAsync(It.IsAny<NpmOpeningMonthActivationRequest>()), Times.Never);
    }
}
