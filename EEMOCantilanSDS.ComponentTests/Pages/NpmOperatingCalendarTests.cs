using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;
using EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;
using EEMOCantilanSDS.Application.Command.Payments.SetNpmOperatingSchedule;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using NpmOperatingCalendar = EEMOCantilanSDS.Client.Components.Pages.Reports.NpmOperatingCalendar;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The NPM operating calendar: every state is named in text, a change is previewed before it is made, a past day belongs to the
/// Head and needs a reason, and the weekly schedule is the Head's alone.
/// </summary>
public sealed class NpmOperatingCalendarTests : TestContext
{
    private const int Year = 2026, Month = 10;
    private readonly Mock<IPaymentsApiClient> _api = new();

    public NpmOperatingCalendarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton(_api.Object);
        _api.Setup(x => x.GetNpmOperatingCalendarAsync(Year, Month)).ReturnsAsync(Result<NpmOperatingCalendarDto>.Success(Calendar()));
        _api.Setup(x => x.GetNpmDayImpactAsync(It.IsAny<DateOnly>())).ReturnsAsync((DateOnly d) =>
            Result<NpmDayImpactDto>.Success(new(d, d < new DateOnly(2026, 10, 6), 12, 4, 8, 120m)));
        _api.Setup(x => x.SetMarketClosureAsync(It.IsAny<SetNpmMarketClosureCommand>())).ReturnsAsync(Result<bool>.Success(true));
        _api.Setup(x => x.ClearMarketClosureAsync(It.IsAny<ClearNpmMarketClosureCommand>())).ReturnsAsync(Result<bool>.Success(true));
        _api.Setup(x => x.SetNpmOperatingScheduleAsync(It.IsAny<SetNpmOperatingScheduleCommand>())).ReturnsAsync(Result<bool>.Success(true));
    }

    private static NpmOperatingCalendarDto Calendar()
    {
        var days = Enumerable.Range(1, 31).Select(d =>
        {
            var date = new DateOnly(Year, Month, d);
            return d switch
            {
                3 => new NpmOperatingDayDto(date, NpmDayState.ClosedByWeeklySchedule, null, null, false),
                4 => new NpmOperatingDayDto(date, NpmDayState.OpenedByOverride, null, "Working Sunday", true),
                5 => new NpmOperatingDayDto(date, NpmDayState.ClosedByOverride, null, "Typhoon", true),
                _ => new NpmOperatingDayDto(date, NpmDayState.Open, null, null, false),
            };
        }).ToList();
        var weekends = new NpmOperatingScheduleDto(new(2026, 9, 1), [DayOfWeek.Saturday, DayOfWeek.Sunday], "Head memorandum", "head");
        return new(Year, Month, days, [weekends], weekends, 2, 29);
    }

    private IRenderedComponent<NpmOperatingCalendar> Render(string role)
    {
        this.AddTestAuthorization().SetAuthorized("user").SetRoles(role);
        return RenderComponent<NpmOperatingCalendar>(p => p.Add(x => x.Year, Year).Add(x => x.Month, Month));
    }

    private static void PickDay(IRenderedComponent<NpmOperatingCalendar> cut, int day) =>
        cut.FindAll(".oc-cell").Single(c => c.QuerySelector(".oc-num")?.TextContent == day.ToString()).Click();

    [Fact]
    public void Every_state_is_named_in_text_not_colour_alone()
    {
        var cut = Render("SuperAdmin");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".oc-cell")));
        Assert.Contains("Closed · weekly", cut.Find("[aria-label^='October 3']").TextContent);
        Assert.Contains("Opened", cut.Find("[aria-label^='October 4']").TextContent);
        Assert.Contains("Closed", cut.Find("[aria-label^='October 5']").TextContent);
        Assert.Contains("2 closed", cut.Find(".oc-summary").TextContent);
        Assert.Contains("closed Saturday, Sunday", cut.Find(".oc-summary").TextContent);
    }

    [Fact]
    public void A_closed_day_offers_open_and_restore_only_when_it_was_overridden()
    {
        var cut = Render("SuperAdmin");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".oc-cell")));

        PickDay(cut, 3);   // closed by the weekly schedule, no override
        cut.WaitForAssertion(() => Assert.Contains("12 billable accounts", cut.Markup));
        var buttons = cut.FindAll(".oc-actions button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Open this day", buttons);
        Assert.DoesNotContain("Restore default", buttons);
        Assert.DoesNotContain("Close this day", buttons);

        PickDay(cut, 5);   // an explicit closure
        cut.WaitForAssertion(() => Assert.Contains("Restore default", cut.Find(".oc-actions").TextContent));
    }

    [Fact]
    public void Opening_a_day_that_has_not_passed_sends_the_clear_command_and_tells_the_page()
    {
        var changed = 0;
        this.AddTestAuthorization().SetAuthorized("a").SetRoles("Admin");
        var cut = RenderComponent<NpmOperatingCalendar>(p => p
            .Add(x => x.Year, Year).Add(x => x.Month, Month)
            .Add(x => x.OnChanged, () => changed++));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".oc-cell")));

        PickDay(cut, 10);   // an open future day: offer to close it
        cut.WaitForAssertion(() => Assert.Contains("Close this day", cut.Markup));
        cut.Find(".oc-actions .oc-btn-primary").Click();

        _api.Verify(x => x.SetMarketClosureAsync(It.Is<SetNpmMarketClosureCommand>(c => c.Date == new DateOnly(Year, Month, 10))), Times.Once);
        cut.WaitForAssertion(() => Assert.Equal(1, changed));
    }

    [Fact]
    public void A_past_day_is_the_Heads_to_change_and_the_Admin_is_told_so()
    {
        var cut = Render("Admin");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".oc-cell")));

        PickDay(cut, 2);   // before the stubbed "today" of October 6
        cut.WaitForAssertion(() => Assert.Contains("can only be changed by the Head", cut.Markup));
        Assert.Empty(cut.FindAll(".oc-actions button"));
    }

    [Fact]
    public void A_past_day_needs_a_reason_before_the_Head_can_change_it()
    {
        var cut = Render("SuperAdmin");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".oc-cell")));

        PickDay(cut, 2);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".oc-actions .oc-btn-primary")));
        cut.Find(".oc-actions .oc-btn-primary").Click();

        Assert.Contains("needs a reason", cut.Find("[role=alert]").TextContent);
        _api.Verify(x => x.SetMarketClosureAsync(It.IsAny<SetNpmMarketClosureCommand>()), Times.Never);

        cut.Find(".oc-day input[type=text]").Input("Flood closed the market");
        cut.Find(".oc-actions .oc-btn-primary").Click();
        _api.Verify(x => x.SetMarketClosureAsync(It.Is<SetNpmMarketClosureCommand>(c => c.Remarks == "Flood closed the market")), Times.Once);
    }

    [Fact]
    public void The_Admin_does_not_see_the_weekly_schedule_editor()
    {
        var admin = Render("Admin");
        admin.WaitForAssertion(() => Assert.NotEmpty(admin.FindAll(".oc-cell")));
        Assert.Empty(admin.FindAll(".oc-sched"));
    }

    [Fact]
    public void The_Head_sees_the_weekly_schedule_and_its_versions()
    {
        var head = Render("SuperAdmin");
        head.WaitForAssertion(() => Assert.NotEmpty(head.FindAll(".oc-sched")));
        Assert.Contains("Head memorandum", head.Find(".oc-history").TextContent);
    }

    [Fact]
    public void The_schedule_needs_an_authority_and_cannot_close_every_day()
    {
        var cut = Render("SuperAdmin");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".oc-sched")));
        cut.FindAll(".oc-sched-head button").Single().Click();

        cut.FindAll(".oc-fieldset button").Single(b => b.TextContent == "Weekends").Click();
        cut.FindAll(".oc-sched .oc-btn-primary").Single().Click();
        Assert.Contains("authority", cut.Find(".oc-sched [role=alert]").TextContent);
        _api.Verify(x => x.SetNpmOperatingScheduleAsync(It.IsAny<SetNpmOperatingScheduleCommand>()), Times.Never);

        cut.Find(".oc-sched input[maxlength='300']").Input("Memo 12");
        cut.FindAll(".oc-sched .oc-btn-primary").Single().Click();
        _api.Verify(x => x.SetNpmOperatingScheduleAsync(It.Is<SetNpmOperatingScheduleCommand>(c =>
            c.ClosedWeekdays.Count == 2 && c.Remarks == "Memo 12")), Times.Once);
    }

    [Fact]
    public void A_failed_load_shows_an_error_with_retry_instead_of_an_empty_calendar()
    {
        _api.Setup(x => x.GetNpmOperatingCalendarAsync(Year, Month)).ReturnsAsync(Result<NpmOperatingCalendarDto>.Failure("Offline"));
        var cut = Render("SuperAdmin");
        cut.WaitForAssertion(() => Assert.Contains("Offline", cut.Find("[role=alert]").TextContent));
        Assert.Empty(cut.FindAll(".oc-cell"));
    }
}

/// <summary>The office-collections hub reads recorded activity and links to the source workspace; it posts nothing.</summary>
public sealed class OfficeCollectionsPanelTests : TestContext
{
    [Fact]
    public void Lists_recorded_activity_for_the_chosen_source_and_links_to_its_workspace()
    {
        var office = new Mock<IOfficeSourcesApiClient>();
        var row = new SourceNativeActivityDto(Guid.NewGuid(), "TRM-0001", new(2026, 10, 6), DateTime.UtcNow, "TERMINAL", null, null,
            "Walk-in", Guid.NewGuid(), "Collector One", 150m, 150m, "Posted", null, null, null, null, null);
        office.Setup(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "TERMINAL"))
            .ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Success([row]));
        AddShell(office);

        var cut = RenderComponent<OfficeCollectionsPanel>();

        cut.WaitForAssertion(() => Assert.Contains("TRM-0001", cut.Markup));
        Assert.Contains("Collector One", cut.Markup);
        Assert.Equal("/operations/terminal", cut.Find(".oo-link").GetAttribute("href"));
        Assert.Contains("may not have synced", cut.Find(".oo-warn").TextContent);
        // Read-only: the hub only looks up activity and posts nothing.
        office.Verify(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), "TERMINAL"), Times.Once);
        office.VerifyNoOtherCalls();
    }

    private void AddShell(Mock<IOfficeSourcesApiClient> office)
    {
        Services.AddSingleton(office.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
    }

    [Fact]
    public void An_empty_day_says_so_and_a_failure_offers_retry()
    {
        var office = new Mock<IOfficeSourcesApiClient>();
        office.SetupSequence(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>()))
            .ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Success([]))
            .ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Failure("Offline"));
        AddShell(office);

        var cut = RenderComponent<OfficeCollectionsPanel>();
        cut.WaitForAssertion(() => Assert.Contains("No Terminal collection is recorded", cut.Find("[role=status]").TextContent));

        cut.Find("select").Change("MARKET_FEES");
        cut.WaitForAssertion(() => Assert.Contains("Offline", cut.Find("[role=alert]").TextContent));
    }
}
