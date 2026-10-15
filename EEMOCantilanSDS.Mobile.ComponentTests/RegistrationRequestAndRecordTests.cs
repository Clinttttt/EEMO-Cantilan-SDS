using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Shared;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class RegistrationRequestAndRecordTests : TestContext
{
    [Fact]
    public void Legacy_identity_partial_payment_and_absent_receipt_are_preserved_without_inventing_SRC()
    {
        var opened = false;
        var view = RenderComponent<CollectionRecordRow>(p => p.Add(x => x.Identity, "Ana Reyes")
            .Add(x => x.Amount, 870m).Add(x => x.Assessed, 900m).Add(x => x.DateText, "Oct 8 · 29 days")
            .Add(x => x.ReceiptEvidence, "No linked receipt").Add(x => x.OnOpen, () => opened = true));
        Assert.Contains("870.00", view.Find(".crr-amount").TextContent);
        Assert.Contains("900.00 assessed", view.Markup);
        Assert.DoesNotContain("SRC-", view.Markup);
        Assert.Contains("No linked receipt", view.Markup);
        view.Find("button").Click(); Assert.True(opened);
    }

    [Fact]
    public void Canonical_identity_is_shown_only_when_supplied_and_missing_amount_is_not_zero()
    {
        var view = RenderComponent<CollectionRecordRow>(p => p.Add(x => x.Identity, "SRC-2026-000009").Add(x => x.Amount, 33m));
        Assert.Contains("SRC-2026-000009", view.Markup); Assert.Contains("33.00", view.Markup);
        view.SetParametersAndRender(p => p.Add(x => x.Identity, "Legacy holder").Add(x => x.Amount, (decimal?)null));
        Assert.DoesNotContain("SRC-", view.Markup); Assert.DoesNotContain("0.00", view.Find(".crr-amount").TextContent);
    }

    [Fact]
    public void Request_retry_keeps_identity_and_evidence_and_never_calls_financial_writer()
    {
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        var sent = new List<SubmitNpmRegistrationRequest>();
        api.Setup(x => x.SubmitNpmRequestAsync(It.IsAny<SubmitNpmRegistrationRequest>())).Callback<SubmitNpmRegistrationRequest>(sent.Add)
            .ReturnsAsync(Result<NpmRegistrationRequestDto>.Failure("Network response unavailable"));
        api.Setup(x => x.SuggestNpmStallAsync(MarketSection.FishSection))
            .ReturnsAsync(Result<NpmStallNumberSuggestionDto>.Success(new(MarketSection.FishSection, "F-13")));
        Services.AddSingleton(api.Object);
        var vendor = Guid.NewGuid();
        var view = RenderComponent<NpmRegistrationRequestForm>(p => p.Add(x => x.VendorId, vendor));
        Assert.Contains("No cash before approval", view.Markup); Assert.Contains("issues no SRC", view.Markup);
        view.Find("button").Click();
        view.Find("input").Input("F-12");
        view.Find(".mc-trigger").Click();
        view.FindAll(".mc-option").Single(x => x.TextContent.Contains("Fish Area")).Click();
        view.Find("textarea").Input("Office reference 42");
        view.Find("button.npm-request-action").Click();
        view.WaitForAssertion(() => Assert.Contains("Network response unavailable", view.Markup));
        view.Find("button.npm-request-action").Click();
        Assert.Equal(2, sent.Count); Assert.Equal(sent[0], sent[1]); Assert.Equal(vendor, sent[0].VendorRegistrationId);
        Assert.Equal("F-12", view.Find("input").GetAttribute("value"));
        Assert.Equal("Office reference 42", sent[0].Note);
        api.Verify(x => x.SuggestNpmStallAsync(MarketSection.FishSection), Times.Once);
        api.Verify(x => x.SubmitNpmRequestAsync(It.IsAny<SubmitNpmRegistrationRequest>()), Times.Exactly(2));
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Request_disables_duplicate_submission_until_server_response()
    {
        var api = new Mock<IMobileApiClient>();
        var pending = new TaskCompletionSource<Result<NpmRegistrationRequestDto>>();
        api.Setup(x => x.SubmitNpmRequestAsync(It.IsAny<SubmitNpmRegistrationRequest>())).Returns(pending.Task);
        Services.AddSingleton(api.Object);
        var view = RenderComponent<NpmRegistrationRequestForm>(p => p.Add(x => x.VendorId, Guid.NewGuid()));
        view.Find("button").Click(); view.Find("input").Input("F-1"); view.Find("textarea").Input("Verify tenancy");
        var saving = view.Find("button.npm-request-action").ClickAsync(new());
        view.WaitForAssertion(() => Assert.True(view.Find("button.npm-request-action").HasAttribute("disabled")));
        api.Verify(x => x.SubmitNpmRequestAsync(It.IsAny<SubmitNpmRegistrationRequest>()), Times.Once);
        pending.SetResult(Result<NpmRegistrationRequestDto>.Failure("Retry")); await saving;
    }
}
