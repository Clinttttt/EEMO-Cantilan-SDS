using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// An occupancy with no Business Payor is listed as needing one; the office links an existing Payor or creates one, always by an explicit
/// press. A same-named Payor is only ever a candidate, and a duplicate create needs the office to say it is a different person.
/// </summary>
public sealed class BusinessPayorsTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly Guid PayorId = Guid.NewGuid();
    private readonly Mock<IBusinessPayorsApiClient> _api = new();

    public BusinessPayorsTests()
    {
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _api.Setup(x => x.GetOccupanciesAsync(It.IsAny<string?>(), PayorLinkFilter.NeedsPayor)).ReturnsAsync(
            Result<IReadOnlyList<PayorOccupancyDto>>.Success([new(ContractId, Guid.NewGuid(), "NPM", "1", "Lisa Ilogans", "Lisa Ilogans", null, null)]));
        _api.Setup(x => x.GetOccupanciesAsync(It.IsAny<string?>(), PayorLinkFilter.Linked)).ReturnsAsync(
            Result<IReadOnlyList<PayorOccupancyDto>>.Success([new(Guid.NewGuid(), Guid.NewGuid(), "NPM", "2", "Lorna Santiago", null, Guid.NewGuid(), "Lorna Santiago")]));
    }

    [Fact]
    public void OccupanciesWithoutAPayor_AreListedAsNeedingOne_AndLinkedOnesShowTheirPayor()
    {
        var cut = RenderComponent<BusinessPayors>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tbody tr"));
            Assert.Contains("NPM · 1", row.TextContent);
            Assert.Contains("Lisa Ilogans", row.TextContent);
            Assert.Contains("Needs Payor", row.TextContent);
            Assert.Contains("Link Payor", row.TextContent);
        }, Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Linked").Click();
        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tbody tr"));
            Assert.Contains("Lorna Santiago", row.TextContent);
            Assert.DoesNotContain("Link Payor", row.TextContent);
        }, Timeout);
    }

    [Fact]
    public void LinkingAnExistingPayor_IsTheOfficesExplicitPress_AndShowsWhereItIsUsed()
    {
        _api.Setup(x => x.SearchPayorsAsync("Lisa Ilogans")).ReturnsAsync(Result<IReadOnlyList<PayorCandidateDto>>.Success(
            [new(PayorId, "Lisa Ilogans", BusinessPayorKind.Person, ["Kanmanggay Space Rental · K-9"])]));
        LinkPayorRequest? sent = null;
        _api.Setup(x => x.LinkAsync(It.IsAny<LinkPayorRequest>())).Callback<LinkPayorRequest>(r => sent = r)
            .ReturnsAsync(Result<PayorLinkOutcomeDto>.Success(new(ContractId, PayorId, "Lisa Ilogans", false)));

        var cut = RenderComponent<BusinessPayors>();
        cut.WaitForAssertion(() => cut.Find("tbody button"), Timeout);
        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Find("form[aria-label='Find a Business Payor']"), Timeout);
        Assert.Null(sent);                                                    // opening the dialog links nothing
        cut.Find("form[aria-label='Find a Business Payor']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Kanmanggay Space Rental · K-9", cut.Find("[aria-label='Business Payors found']").TextContent);
            Assert.Null(sent);                                                // a candidate is only a candidate
        }, Timeout);
        cut.Find("[aria-label='Business Payors found'] button").Click();
        Assert.Null(sent);                                                    // choosing a candidate only highlights it
        Assert.Equal("true", cut.Find("[aria-label='Business Payors found'] button").GetAttribute("aria-selected"));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Link Payor" && b.ClassList.Contains("v3-btn-primary")).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal((ContractId, PayorId), (sent!.ContractId, sent.PayorId));
            Assert.Contains("is now the Business Payor of NPM 1", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void CreatingADuplicateNamedPayor_NeedsTheOfficeToConfirmADifferentPerson()
    {
        _api.Setup(x => x.SearchPayorsAsync(It.IsAny<string>())).ReturnsAsync(Result<IReadOnlyList<PayorCandidateDto>>.Success(
            [new(PayorId, "Lisa Ilogans", BusinessPayorKind.Person, [])]));
        var requests = new List<CreatePayorAndLinkRequest>();
        _api.Setup(x => x.CreateAndLinkAsync(It.IsAny<CreatePayorAndLinkRequest>()))
            .Returns<CreatePayorAndLinkRequest>(r =>
            {
                requests.Add(r);
                return Task.FromResult(r.ConfirmDuplicate
                    ? Result<PayorLinkOutcomeDto>.Success(new(ContractId, Guid.NewGuid(), "Lisa Ilogans", true))
                    : Result<PayorLinkOutcomeDto>.Failure("DUPLICATE_PAYOR: A Business Payor named Lisa Ilogans already exists.", ResultStatus.Conflict));
            });

        var cut = RenderComponent<BusinessPayors>();
        cut.WaitForAssertion(() => cut.Find("tbody button"), Timeout);
        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Create and link"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Create and link").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("already exists", cut.Find("[role='alert']").TextContent);
            Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Create and link").HasAttribute("disabled"));
        }, Timeout);
        cut.Find(".lpd-confirm input").Change(true);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Create and link").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { false, true }, requests.Select(r => r.ConfirmDuplicate));
            Assert.Contains("is now the Business Payor", cut.Markup);
        }, Timeout);
    }
}
