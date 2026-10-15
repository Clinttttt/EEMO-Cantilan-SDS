using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class NpmStallSuggestionTests : TestContext
{
    [Theory]
    [InlineData("1,2,3", "4")]
    [InlineData("F-11,F-12", "F-13")]
    [InlineData("01,02", "03")]
    [InlineData("SP-1,SP-2,5", "6")]
    [InlineData("F-12,M-17", "18")]
    [InlineData("A,B", null)]
    public void Numbering_matches_the_office_section_series(string existing, string? expected)
        => Assert.Equal(expected, NpmStallNumberSuggestion.Next(existing.Split(',')));

    [Fact]
    public void Empty_section_starts_from_one_but_never_guarantees_stall_availability()
        => Assert.Equal("1", NpmStallNumberSuggestion.Next([]));

    [Fact]
    public void Choosing_section_autofills_server_suggestion_without_locking_stall_number()
    {
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.SuggestNpmStallAsync(MarketSection.FishSection))
            .ReturnsAsync(Result<NpmStallNumberSuggestionDto>.Success(new(MarketSection.FishSection, "F-13")));
        Services.AddSingleton(api.Object);
        var cut = RenderComponent<NpmRegistrationRequestForm>(x => x.Add(p => p.VendorId, Guid.NewGuid()));
        cut.Find("button.npm-request-action").Click();
        cut.Find(".mc-trigger").Click();
        cut.FindAll(".mc-option").Single(x => x.TextContent.Contains("Fish Area")).Click();
        cut.WaitForAssertion(() => Assert.Equal("F-13", cut.Find("input").GetAttribute("value")));
        Assert.Contains("Suggested next number", cut.Markup);
        cut.Find("input").Input("F-12");
        Assert.Equal("F-12", cut.Find("input").GetAttribute("value"));
        api.Verify(x => x.SuggestNpmStallAsync(MarketSection.FishSection), Times.Once);
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public void Missing_older_API_keeps_editable_input_without_making_up_number()
    {
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.SuggestNpmStallAsync(MarketSection.MeatSection))
            .ReturnsAsync(Result<NpmStallNumberSuggestionDto>.NotFound());
        Services.AddSingleton(api.Object);
        var cut = RenderComponent<NpmRegistrationRequestForm>(x => x.Add(p => p.VendorId, Guid.NewGuid()));
        cut.Find("button.npm-request-action").Click();
        cut.Find(".mc-trigger").Click();
        cut.FindAll(".mc-option").Single(x => x.TextContent.Contains("Meat Area")).Click();
        cut.WaitForAssertion(() => Assert.Contains("subject to office verification", cut.Markup));
        Assert.Equal("", cut.Find("input").GetAttribute("value"));
        cut.Find("input").Input("M-4");
        Assert.Equal("M-4", cut.Find("input").GetAttribute("value"));
    }
}
