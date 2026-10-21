using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class NpmPendingRequestTests : TestContext
{
    private static NpmRegistrationRequestDto Pending(Guid vendor) => new(
        Guid.NewGuid(), vendor, 2026, "Josephine Reyes", Guid.NewGuid(), "Collector",
        "2", MarketSection.FishSection, "Office evidence", DateTime.UtcNow,
        NpmRegistrationRequestStatus.Submitted, 1, null, null, null, null, null);

    [Fact]
    public void Returning_to_vendor_recovers_server_pending_request_without_new_submission_action()
    {
        var vendor = Guid.NewGuid();
        var row = Pending(vendor);
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetPendingNpmRequestAsync(vendor))
            .ReturnsAsync(Result<NpmRegistrationRequestDto>.Success(row));
        Services.AddSingleton(api.Object);

        var first = RenderComponent<NpmRegistrationRequestForm>(x => x.Add(p => p.VendorId, vendor));
        first.WaitForAssertion(() => Assert.Contains("Pending review", first.Markup));
        Assert.Contains("stall", first.Markup);
        Assert.Contains("2", first.Markup);
        Assert.Empty(first.FindAll(".npm-request-action"));
        Assert.Contains("My NPM requests", first.Markup);
        first.Dispose();

        var returned = RenderComponent<NpmRegistrationRequestForm>(x => x.Add(p => p.VendorId, vendor));
        returned.WaitForAssertion(() => Assert.Contains("Pending review", returned.Markup));
        Assert.Empty(returned.FindAll(".npm-request-action"));
        api.Verify(x => x.GetPendingNpmRequestAsync(vendor), Times.Exactly(2));
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public void Unavailable_status_check_disables_new_submission_until_server_confirms_status()
    {
        var vendor = Guid.NewGuid();
        var result = Result<NpmRegistrationRequestDto>.Failure("Office is unreachable.", ResultStatus.Failed);
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetPendingNpmRequestAsync(vendor)).ReturnsAsync(() => result);
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<NpmRegistrationRequestForm>(x => x.Add(p => p.VendorId, vendor));
        cut.WaitForAssertion(() => Assert.Contains("Office is unreachable.", cut.Markup));
        Assert.DoesNotContain("Request NPM registration</button>", cut.Markup);
        Assert.Contains("Check status again", cut.Markup);
        result = Result<NpmRegistrationRequestDto>.NotFound();
        cut.Find("button.npm-request-action").Click();
        cut.WaitForAssertion(() => Assert.Contains("Request NPM registration", cut.Markup));
        api.Verify(x => x.GetPendingNpmRequestAsync(vendor), Times.Exactly(2));
    }

    [Fact]
    public void A_conflict_fetches_pending_state_and_removes_redundant_form()
    {
        var vendor = Guid.NewGuid();
        var calls = 0;
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetPendingNpmRequestAsync(vendor)).ReturnsAsync(() =>
            ++calls == 1 ? Result<NpmRegistrationRequestDto>.NotFound()
                : Result<NpmRegistrationRequestDto>.Success(Pending(vendor)));
        api.Setup(x => x.SubmitNpmRequestAsync(It.IsAny<SubmitNpmRegistrationRequest>()))
            .ReturnsAsync(Result<NpmRegistrationRequestDto>.Failure("Already pending", ResultStatus.Conflict));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<NpmRegistrationRequestForm>(x => x.Add(p => p.VendorId, vendor));
        cut.Find("button.npm-request-action").Click();
        cut.Find("input").Input("2");
        cut.Find("textarea").Input("Tenancy evidence");
        cut.Find("button.npm-request-action").Click();
        cut.WaitForAssertion(() => Assert.Contains("Pending review", cut.Markup));
        Assert.Empty(cut.FindAll(".npm-request-action"));
        api.Verify(x => x.SubmitNpmRequestAsync(It.IsAny<SubmitNpmRegistrationRequest>()), Times.Once);
        api.Verify(x => x.GetPendingNpmRequestAsync(vendor), Times.Exactly(2));
    }
}
