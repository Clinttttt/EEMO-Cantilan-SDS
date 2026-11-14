using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Notifications;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The office's inbox states what the server says, and a failed read is never shown as an empty inbox.</summary>
public sealed class NotificationsTests : TestContext
{
    private static readonly InboxItemDto Unread = new(Guid.NewGuid(), "Online payment needs review", "The gateway reported an amount.", "/online-payments", new DateTime(2026, 11, 5, 1, 0, 0, DateTimeKind.Utc), false);
    private static readonly InboxItemDto Seen = new(Guid.NewGuid(), "Late penalties added", "3 penalties.", "/operations/fines", new DateTime(2026, 11, 4, 1, 0, 0, DateTimeKind.Utc), true);
    private readonly Mock<IInboxApiClient> _api = new();

    private IRenderedComponent<Notifications> Open(Result<InboxPageDto> page)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Staff").SetRoles("Admin");
        _api.Setup(a => a.GetAsync()).ReturnsAsync(page);
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        return RenderComponent<Notifications>();
    }

    private static Result<InboxPageDto> Page(int unread, params InboxItemDto[] items) => Result<InboxPageDto>.Success(new(items, unread));

    [Fact]
    public void ThePage_IsForTheHeadAndAdmins()
    {
        Assert.Equal("/notifications", typeof(Notifications).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("SuperAdmin,Admin", typeof(Notifications).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    [Fact]
    public void AMessage_IsListedWithItsTimeInTheOfficesClock_AndNewOnesSaySoInWords()
    {
        var cut = Open(Page(1, Unread, Seen));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".ib-row").Count));
        Assert.Contains("1 unread", cut.Find("h2").TextContent);
        Assert.Contains("New", cut.FindAll(".ib-row")[0].TextContent);
        Assert.DoesNotContain("New", cut.FindAll(".ib-row")[1].TextContent);
        Assert.Contains("Nov 5, 2026 9:00 AM", cut.FindAll(".ib-row")[0].TextContent);
        Assert.Single(cut.FindAll(".ib-read"));
    }

    [Fact]
    public void AFailedRead_SaysSo_AndTryAgainAsksAgain()
    {
        var cut = Open(Result<InboxPageDto>.Failure("Down"));

        cut.WaitForAssertion(() => Assert.Contains("couldn't be loaded", cut.Find("[role=alert]").TextContent));
        Assert.Empty(cut.FindAll(".ib-empty"));

        _api.Setup(a => a.GetAsync()).ReturnsAsync(Page(0));
        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".ib-empty")));
        Assert.Contains("All read", cut.Find("h2").TextContent);
        Assert.Empty(cut.FindAll("button.v3-btn"));
    }

    [Fact]
    public void MarkAll_SendsNoId_AndTheCountComesFromTheServer()
    {
        _api.Setup(a => a.MarkReadAsync(null)).ReturnsAsync(Page(0, Unread with { Read = true }));
        var cut = Open(Page(1, Unread));

        cut.WaitForAssertion(() => cut.Find("button.v3-btn").Click());

        cut.WaitForAssertion(() => Assert.Contains("All read", cut.Find("h2").TextContent));
        _api.Verify(a => a.MarkReadAsync(null), Times.Once);
    }

    [Fact]
    public void AFailedSave_SaysSo_AndKeepsTheList()
    {
        _api.Setup(a => a.MarkReadAsync(Unread.Id)).ReturnsAsync(Result<InboxPageDto>.Failure("No"));
        var cut = Open(Page(1, Unread));

        cut.WaitForAssertion(() => cut.Find(".ib-read").Click());

        cut.WaitForAssertion(() => Assert.Contains("No", cut.Find("[role=alert]").TextContent));
        Assert.Single(cut.FindAll(".ib-row"));
        Assert.Contains("1 unread", cut.Find("h2").TextContent);
    }

    [Fact]
    public void Open_SavesTheMessageAsRead_ThenGoesToItsPage()
    {
        _api.Setup(a => a.MarkReadAsync(Unread.Id)).ReturnsAsync(Page(0, Unread with { Read = true }));
        var cut = Open(Page(1, Unread));

        cut.WaitForAssertion(() => cut.Find("a.v3-btn").Click());

        cut.WaitForAssertion(() => Assert.EndsWith("/online-payments", Services.GetRequiredService<NavigationManager>().Uri));
        _api.Verify(a => a.MarkReadAsync(Unread.Id), Times.Once);
    }
}
