using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class NpmMonthReviewActionsTests : TestContext
{
    private readonly Guid _stall = Guid.NewGuid();
    private readonly DateOnly _period = new(2025, 8, 1);
    private readonly Mock<IDailyCollectionApiClient> _api = new();
    private IRenderedComponent<NpmMonthReviewActions> Render(bool enabled = true, NpmOpeningMonthRateEvidence? authority = null)
    {
        _api.Setup(x => x.IsNpmMonthlySettlementAvailableAsync()).ReturnsAsync(Result<bool>.Success(enabled));
        _api.Setup(x => x.ReviewNpmMonthAsync(_stall, _period)).ReturnsAsync(Result<NpmRentMonthHeadReview>.Success(new(
            new(Guid.NewGuid(), _stall, Guid.NewGuid(), _period, new(2026, 10, 17), NpmRentPaymentScope.WholeMonth,
                4, 900, 0, 875, 25, 25, 0, "frozen-token", RevenueInstrumentType.OfficialReceipt, OpeningAuthority: authority), [], [])));
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(new EEMOCantilanSDS.Client.Services.BrandingState(Mock.Of<IMunicipalitiesApiClient>()));
        return RenderComponent<NpmMonthReviewActions>(p => p.Add(x => x.StallId, _stall).Add(x => x.PeriodStart, _period));
    }
    [Fact]
    public void Prospective_month_shows_server_approved_rate_evidence_separately_from_remaining_cash()
    {
        var authority = new NpmOpeningMonthRateEvidence("ApprovedFullOpeningMonthV1", Guid.NewGuid(), Guid.NewGuid(), _stall,
            Guid.NewGuid(), new(2025, 8, 20), new(2025, 8, 20), Guid.NewGuid(), new(2025, 1, 1), 900, 30,
            4321, 4567, "Synthetic Head", DateTime.UnixEpoch, Guid.NewGuid(), DateTime.UnixEpoch,
            new(2025, 1, 1), "Synthetic office approval", true, false);
        var cut = Render(authority: authority); cut.Find("button").Click();
        Assert.Contains("Opening-month rate evidence", cut.Markup);
        Assert.Contains("Synthetic office approval", cut.Markup);
        Assert.Contains("30.00", cut.Find(".npm-opening-authority").TextContent);
        Assert.Contains("25.00", cut.Find(".npm-review-position").TextContent);
        _api.Verify(x => x.ApplyNpmMonthCreditAsync(It.IsAny<NpmRentMonthCreditRequest>()), Times.Never);
    }

    [Fact]
    public void Inactive_runtime_exposes_no_review_action_or_credit_request()
    {
        var cut = Render(false);
        Assert.Empty(cut.FindAll("button"));
        _api.Verify(x => x.ReviewNpmMonthAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>()), Times.Never);
    }
    [Fact]
    public void Drawer_shows_exact_authoritative_balance_and_requires_explicit_review()
    {
        var cut = Render(); cut.Find("button").Click();
        Assert.Contains("25.00", cut.Markup);
        Assert.Contains("Whole-period", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        Assert.NotNull(cut.Find("[role=dialog]"));
    }
    [Fact]
    public void Synthetic_review_preview_uses_the_real_drawer_without_starting_a_service()
    {
        var cut = Render(); cut.Find("button").Click();
        Assert.Contains("25.00", cut.Markup);
        _api.Verify(x => x.ApplyNpmMonthCreditAsync(It.IsAny<NpmRentMonthCreditRequest>()), Times.Never);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var css = string.Join("\n", new[] { "NpmMonthReviewActions", "VendorDrawerShell" }
            .Select(name => File.ReadAllText(Path.Combine(root, $"EEMOCantilanSDS.Client/Components/Shared/{name}.razor.css"))));
        var directory = Path.Combine(Path.GetTempPath(), "stalltrack-npm-acceptance"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "head-review.html"),
            "<!doctype html><meta charset='utf-8'><title>Synthetic Head review - no service</title><style>" + css +
            "body{font-family:Arial,sans-serif}:root{--navy:#173b61;--border:#cfd6de;--bg-card:white;--text:#172b42}</style>" + cut.Markup);
    }
    [Fact]
    public async Task Confirmed_stale_review_preserves_fields_and_requires_explicit_confirmation_again()
    {
        var requests = new List<NpmRentMonthCreditRequest>();
        _api.Setup(x => x.ApplyNpmMonthCreditAsync(It.IsAny<NpmRentMonthCreditRequest>()))
            .Returns((NpmRentMonthCreditRequest request) => {
                requests.Add(request);
                return Task.FromResult(Result<NpmRentMonthCreditOutcome>.ValidationFailure(new()
                    { ["ExpectedVersion"] = ["The month changed. No credit was revised."] }));
            });
        var cut = Render(); cut.Find("button").Click();
        cut.Find("#npm-review-amount").Change("25");
        cut.Find("#npm-review-reference").Change("Approved decision");
        cut.Find("#npm-review-reason").Change("Verified evidence");
        cut.Find("input[type=checkbox]").Change(true);
        await cut.Find("footer .btn-primary").ClickAsync(new());
        Assert.Single(requests);
        Assert.False(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        Assert.Equal("Approved decision", cut.Find("#npm-review-reference").GetAttribute("value"));
        Assert.Contains("No credit was revised", cut.Markup);
        _api.Verify(x => x.ReviewNpmMonthAsync(_stall, _period), Times.Exactly(2));
    }

    [Fact]
    public async Task Lost_response_preserves_frozen_credit_and_retries_the_same_operation()
    {
        var requests = new List<NpmRentMonthCreditRequest>();
        _api.Setup(x => x.ApplyNpmMonthCreditAsync(It.IsAny<NpmRentMonthCreditRequest>()))
            .Returns((NpmRentMonthCreditRequest r) => {
                requests.Add(r);
                return requests.Count == 1 ? Task.FromException<Result<NpmRentMonthCreditOutcome>>(new HttpRequestException("lost response"))
                    : Task.FromResult(Result<NpmRentMonthCreditOutcome>.Success(new(5, true)));
            });
        var cut = Render(); cut.Find("button").Click();
        cut.Find("#npm-review-amount").Change("25");
        cut.Find("#npm-review-reference").Change("Approved decision");
        cut.Find("#npm-review-reason").Change("Verified office evidence");
        cut.Find("input[type=checkbox]").Change(true);
        await cut.Find("footer .btn-primary").ClickAsync(new());
        Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.Contains("Outcome not confirmed", cut.Markup);
        await cut.Find("footer .btn-primary").ClickAsync(new());
        Assert.Equal(2, requests.Count); Assert.Equal(requests[0], requests[1]);
        Assert.Contains("Reviewed action confirmed", cut.Markup);
    }
}
