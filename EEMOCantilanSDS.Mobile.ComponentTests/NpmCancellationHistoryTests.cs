using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class NpmCancellationHistoryTests : TestContext
{
    [Fact]
    public void Pending_cancel_needs_confirmation_and_keeps_a_visible_cancelled_history()
    {
        var id = Guid.NewGuid();
        var pending = new NpmRegistrationRequestDto(id, Guid.NewGuid(), 2026, "Josephine Reyes",
            Guid.NewGuid(), "Collector", "2", MarketSection.FishSection, "Office note",
            DateTime.UtcNow, NpmRegistrationRequestStatus.Submitted, 1, null, null, null, null, null);
        var cancelled = pending with { Status = NpmRegistrationRequestStatus.Cancelled, Version = 2 };
        var page = new NpmRegistrationRequestPage([pending], 1, 1, 25);
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetNpmRequestsAsync(1, 25)).ReturnsAsync(() =>
            Result<NpmRegistrationRequestPage>.Success(page));
        api.Setup(x => x.CancelNpmRequestAsync(id, It.IsAny<CancelNpmRegistrationRequest>()))
            .ReturnsAsync(Result<NpmRegistrationRequestDto>.Success(cancelled));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new MobileSessionService());
        var cut = RenderComponent<NpmRequests>();
        cut.WaitForAssertion(() => Assert.Contains("Pending review", cut.Markup));
        Assert.Empty(cut.FindAll(".npm-history a[href='/records'], .npm-history-safeguard, .npm-history-list-head h2"));
        Assert.Contains("1 total", cut.Find(".npm-history-list-head").TextContent);
        Assert.Contains("Fish Area", cut.Markup);
        cut.Find(".npm-history-cancel-trigger").Click();
        Assert.Contains("Cancel this pending request?", cut.Markup);
        cut.Find(".npm-history-cancel-buttons button:last-child").Click();
        api.Verify(x => x.CancelNpmRequestAsync(It.IsAny<Guid>(), It.IsAny<CancelNpmRegistrationRequest>()), Times.Never);
        cut.Find(".npm-history-cancel-trigger").Click();
        page = new([cancelled], 1, 1, 25);
        cut.Find(".npm-history-cancel-confirm").Click();
        cut.WaitForAssertion(() => Assert.Contains("Cancelled", cut.Markup));
        Assert.Empty(cut.FindAll(".npm-history-cancel-trigger"));
        api.Verify(x => x.CancelNpmRequestAsync(id,
            It.Is<CancelNpmRegistrationRequest>(x => x.ExpectedVersion == 1 && x.ClientOperationId != Guid.Empty)), Times.Once);
    }
}
