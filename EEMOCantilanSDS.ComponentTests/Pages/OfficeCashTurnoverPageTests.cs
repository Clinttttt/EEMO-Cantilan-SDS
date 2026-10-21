using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OfficeCashPage = EEMOCantilanSDS.Client.Components.Pages.Menus.OfficeCashTurnover;
using OfficeCashLink = EEMOCantilanSDS.Client.Components.Shared.OfficeCashTurnoverLink;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The Head-facing Office cash turnover page: server figures only, Head-only controls, safe retries and a deliberate void.</summary>
public sealed class OfficeCashTurnoverPageTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid FirstId = Guid.NewGuid(), SecondId = Guid.NewGuid();
    private readonly Mock<IOfficeCashTurnoverApiClient> _api = new();

    private static OfficeCashCollectionDto Collection(Guid id, string src, decimal net, string payer) =>
        new(id, src, PhilippineTime.Today, payer, net, 0m, net, "head");

    private static OfficeCashTurnoverRowDto Row(Guid id, OfficeCashTurnoverStatus status = OfficeCashTurnoverStatus.Recorded, decimal difference = 0m) =>
        new(id, PhilippineTime.Today, PhilippineTime.Today, PhilippineTime.Today, 600m, 600m - difference, difference, difference != 0m, "OCT-2026-000007", "DEP-9", "Cashier",
            status, 1, "head", "actor", DateTime.UtcNow, status == OfficeCashTurnoverStatus.Voided ? "Wrong slip" : null,
            status == OfficeCashTurnoverStatus.Voided ? "head" : null, status == OfficeCashTurnoverStatus.Voided ? "actor" : null,
            status == OfficeCashTurnoverStatus.Voided ? DateTime.UtcNow : null);

    private void Arrange(bool available = true, string role = "SuperAdmin", IReadOnlyList<OfficeCashTurnoverRowDto>? register = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var today = PhilippineTime.Today;
        _api.Setup(x => x.IsAvailableAsync()).ReturnsAsync(Result<bool>.Success(available));
        _api.Setup(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<OfficeCashPositionDto>.Success(
            new(today, today, 1200m, 0m, 1200m, 2, 2, 0m, 0m, 0)));
        _api.Setup(x => x.GetReviewAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<OfficeCashTurnoverReviewDto>.Success(
            new(today, today, [Collection(FirstId, "SRC-2026-000010", 600m, "Ana Reyes"), Collection(SecondId, "SRC-2026-000011", 600m, "Ben Cruz")], 1200m)));
        _api.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<OfficeCashTurnoverRowDto>>.Success(register ?? []));
        _api.Setup(x => x.GetWorkspaceAsync(It.IsAny<OfficeCashFilterRequest>())).Returns(async (OfficeCashFilterRequest request) =>
            Result<OfficeCashWorkspaceDto>.Success(new(
                (await _api.Object.GetPositionAsync(request.From, request.To)).Value!,
                (await _api.Object.GetReviewAsync(request.From, request.To)).Value!,
                (await _api.Object.GetRegisterAsync(request.From, request.To)).Value!, [])));
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        this.AddTestAuthorization().SetAuthorized(role.ToLowerInvariant()).SetRoles(role);
    }

    private IRenderedComponent<OfficeCashPage> Open()
    {
        var page = RenderComponent<OfficeCashPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".v3-table")), Timeout);
        return page;
    }

    [Fact]
    public void Filters_send_stable_authority_and_instrument_and_replace_all_server_figures()
    {
        Arrange();
        var authority = Guid.NewGuid();
        var today = PhilippineTime.Today;
        OfficeCashFilterRequest? sent = null;
        _api.Setup(x => x.GetWorkspaceAsync(It.IsAny<OfficeCashFilterRequest>()))
            .Callback<OfficeCashFilterRequest>(request => sent = request)
            .ReturnsAsync((OfficeCashFilterRequest request) => Result<OfficeCashWorkspaceDto>.Success(new(
                new(today, today, request.AuthorityId is null ? 1200m : 600m, 0m, request.AuthorityId is null ? 1200m : 600m, 1, 1, 0m, 0m, 0),
                new(today, today, [Collection(FirstId, "SRC-2026-000010", 600m, "Ana Reyes")], 600m), [], [new(authority, "Alice Office")] )));
        var page = Open();
        var filters = page.Find(".oc-filters");
        Assert.Equal(new[] { "From", "To", "Authority", "Instrument" },
            filters.QuerySelectorAll("label > span").Select(x => x.TextContent));
        Assert.Contains("Alice Office", filters.TextContent);
        page.FindAll(".oc-filters select")[0].Change(authority.ToString());
        page.FindAll(".oc-filters select")[1].Change("CashTicket");
        page.Find(".oc-filters").Submit();
        page.WaitForAssertion(() => Assert.Equal(authority, sent!.AuthorityId), Timeout);
        Assert.Equal(EEMOCantilanSDS.Domain.Enums.RevenueInstrumentType.CashTicket, sent!.Instrument);
        Assert.DoesNotContain("1,200.00", page.Find("dl[aria-label='Office cash position']").TextContent);
        Assert.Contains("600.00", page.Find("dl[aria-label='Office cash position']").TextContent);
        page.Find("input[aria-label='Select SRC-2026-000010']").Change(true);
        page.Find(".oc-form input[maxlength='150']").Input("Office Cashier");
        Assert.False(page.Find(".oc-record").HasAttribute("disabled"));
        page.FindAll(".oc-filters select")[1].Change("OfficialReceipt");
        Assert.True(page.Find(".oc-record").HasAttribute("disabled"));
        page.FindAll(".oc-filters select")[1].Change("CashTicket");
        Assert.False(page.Find(".oc-record").HasAttribute("disabled"));
        _api.Setup(x => x.GetWorkspaceAsync(It.IsAny<OfficeCashFilterRequest>())).ReturnsAsync(Result<OfficeCashWorkspaceDto>.Failure("Filter unavailable"));
        page.Find(".oc-filters").Submit();
        page.WaitForAssertion(() => Assert.Contains("Filter unavailable", page.Find("[role=alert]").TextContent), Timeout);
        Assert.Empty(page.FindAll("dl[aria-label='Office cash position'], .oc-form"));
    }

    [Fact]
    public void A_municipality_without_the_module_sees_a_notice_and_nothing_loads()
    {
        Arrange(available: false);
        var page = RenderComponent<OfficeCashPage>();

        page.WaitForAssertion(() => Assert.Contains("not enabled", page.Find("p.v3-notice").TextContent), Timeout);
        _api.Verify(x => x.GetReviewAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()), Times.Never);
        _api.Verify(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()), Times.Never);
    }

    [Fact]
    public void The_Head_sees_the_server_position_eligible_collections_and_never_a_deposit_claim()
    {
        Arrange();
        var page = Open();

        var strip = page.Find("dl[aria-label='Office cash position']").TextContent;
        Assert.Contains("1,200.00", strip); Assert.Contains("Covered by turnover", strip); Assert.Contains("Pending", strip);
        Assert.Contains("SRC-2026-000010", page.Markup); Assert.Contains("Ana Reyes", page.Markup);
        Assert.DoesNotContain("not a verified bank deposit or Treasury acknowledgment", page.Markup);
        Assert.Empty(page.FindAll(".oc-form"));                                              // nothing to fill until collections are chosen
    }

    [Fact]
    public void Admin_reads_everything_but_has_no_selection_form_or_void_control()
    {
        Arrange(role: "Admin", register: [Row(Guid.NewGuid())]);
        _api.Setup(x => x.GetDetailAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => Result<OfficeCashTurnoverDetailDto>.Success(
            new(Row(id), null, [new(FirstId, "SRC-2026-000010", 600m, "actor", "head", true)])));
        var page = Open();

        Assert.Empty(page.FindAll("input[type=checkbox]")); Assert.Empty(page.FindAll(".oc-form"));
        Assert.Contains("DEP-9", page.Markup);
        page.Find("button.v3-btn-sm").Click();
        page.WaitForAssertion(() => Assert.Contains("Collections covered", page.Find(".sd-drawer").TextContent), Timeout);
        Assert.DoesNotContain("Void this turnover", page.Find(".sd-drawer").TextContent);
    }

    [Fact]
    public void Recording_defaults_to_the_expected_amount_demands_evidence_and_sends_the_exact_request()
    {
        Arrange();
        RecordOfficeCashTurnoverRequest? sent = null;
        _api.Setup(x => x.RecordAsync(It.IsAny<RecordOfficeCashTurnoverRequest>())).Callback<RecordOfficeCashTurnoverRequest>(r => sent = r)
            .ReturnsAsync(Result<OfficeCashTurnoverDetailDto>.Success(new(Row(Guid.NewGuid()), null, [])));
        var page = Open();

        page.Find("input[aria-label='Select SRC-2026-000010']").Change(true);
        page.Find("input[aria-label='Select SRC-2026-000011']").Change(true);
        Assert.Contains("1,200.00", page.Find(".oc-expected").TextContent);
        Assert.True(page.Find(".oc-form button[type=submit]").HasAttribute("disabled"));                 // no reference or responsible person yet
        page.Find(".oc-form input[maxlength='100']").Input("DEP-9");
        page.Find(".oc-form input[maxlength='150']").Input("Cashier");
        Assert.False(page.Find(".oc-form button[type=submit]").HasAttribute("disabled"));

        page.Find(".oc-form").Submit();
        page.WaitForAssertion(() => Assert.NotNull(sent), Timeout);
        Assert.Equal((1200m, "DEP-9", "Cashier", PhilippineTime.Today), (sent!.AmountTurnedOver, sent.Reference, sent.TurnedOverBy, sent.TurnoverDate));
        Assert.Equal(new[] { FirstId, SecondId }.OrderBy(x => x), sent.CollectionIds!.OrderBy(x => x));
        Assert.NotEqual(Guid.Empty, sent.ClientOperationId);
        page.WaitForAssertion(() => Assert.Contains("Turnover recorded", page.Find("p.v3-notice").TextContent), Timeout);
        _api.Verify(x => x.GetReviewAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()), Times.Exactly(2));  // reloaded from the server after saving
        Assert.Empty(page.FindAll(".oc-form"));                                                          // selection cleared
    }

    [Fact]
    public void A_shortfall_is_shown_and_needs_remarks_before_it_can_be_recorded()
    {
        Arrange();
        var page = Open();
        page.Find("input[aria-label='Select SRC-2026-000010']").Change(true);
        page.Find(".oc-form input[maxlength='100']").Input("DEP-9");
        page.Find(".oc-form input[maxlength='150']").Input("Cashier");

        page.Find(".oc-form input[type=number]").Input("500");
        Assert.Contains("Shortfall", page.Find(".oc-difference").TextContent); Assert.Contains("100.00", page.Find(".oc-difference").TextContent);
        Assert.True(page.Find(".oc-form button[type=submit]").HasAttribute("disabled"));
        page.Find(".oc-form textarea").Input("Counted again at the cashier");
        Assert.False(page.Find(".oc-form button[type=submit]").HasAttribute("disabled"));

        page.Find(".oc-form input[type=number]").Input("650");
        Assert.Contains("Excess", page.Find(".oc-difference").TextContent);
    }

    [Fact]
    public void A_lost_reply_retries_with_the_same_operation_and_a_changed_reference_gets_a_new_one()
    {
        Arrange();
        var sent = new List<RecordOfficeCashTurnoverRequest>();
        _api.Setup(x => x.RecordAsync(It.IsAny<RecordOfficeCashTurnoverRequest>())).Returns((RecordOfficeCashTurnoverRequest r) =>
        {
            sent.Add(r);
            return sent.Count <= 2 ? throw new HttpRequestException("Synthetic lost reply")
                : Task.FromResult(Result<OfficeCashTurnoverDetailDto>.Success(new(Row(Guid.NewGuid()), null, [])));
        });
        var page = Open();
        page.Find("input[aria-label='Select SRC-2026-000010']").Change(true);
        page.Find(".oc-form input[maxlength='100']").Input("DEP-9");
        page.Find(".oc-form input[maxlength='150']").Input("Cashier");

        page.Find(".oc-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("reply was lost", page.Find("p.v3-notice-error").TextContent), Timeout);
        page.Find(".oc-form").Submit();
        page.WaitForAssertion(() => Assert.Equal(2, sent.Count), Timeout);
        Assert.Equal((sent[0].ClientOperationId, sent[0].AmountTurnedOver, sent[0].Reference, sent[0].TurnedOverBy, sent[0].TurnoverDate),
            (sent[1].ClientOperationId, sent[1].AmountTurnedOver, sent[1].Reference, sent[1].TurnedOverBy, sent[1].TurnoverDate));   // same intent, same operation
        Assert.Equal(sent[0].CollectionIds, sent[1].CollectionIds);

        page.Find(".oc-form input[maxlength='100']").Input("DEP-10");                         // a different intent
        page.Find(".oc-form").Submit();
        page.WaitForAssertion(() => Assert.Equal(3, sent.Count), Timeout);
        Assert.NotEqual(sent[1].ClientOperationId, sent[2].ClientOperationId); Assert.Equal("DEP-10", sent[2].Reference);
    }

    [Fact]
    public void Voiding_is_deliberate_needs_a_reason_and_keeps_the_record_visible()
    {
        var id = Guid.NewGuid();
        Arrange(register: [Row(id)]);
        _api.Setup(x => x.GetDetailAsync(id)).ReturnsAsync(Result<OfficeCashTurnoverDetailDto>.Success(
            new(Row(id), "Counted at the cashier", [new(FirstId, "SRC-2026-000010", 600m, "actor", "head", true)])));
        string? reason = null;
        _api.Setup(x => x.VoidAsync(id, It.IsAny<VoidOfficeCashTurnoverRequest>())).Callback<Guid, VoidOfficeCashTurnoverRequest>((_, r) => reason = r.Reason)
            .ReturnsAsync(Result<OfficeCashTurnoverDetailDto>.Success(new(Row(id, OfficeCashTurnoverStatus.Voided), "Counted at the cashier",
                [new(FirstId, "SRC-2026-000010", 600m, "actor", "head", false)])));
        var page = Open();

        page.Find("button.v3-btn-sm").Click();
        page.WaitForAssertion(() => Assert.Contains("Collections covered", page.Find(".sd-drawer").TextContent), Timeout);
        Assert.Contains("Active", page.Find("table.oc-coverage").TextContent);
        Assert.Empty(page.FindAll(".oc-reason"));                                             // nothing to fill until the Head chooses to void
        page.FindAll(".sd-drawer .v3-btn-sm").Single(b => b.TextContent.Contains("Void this turnover")).Click();
        Assert.True(page.Find(".sd-drawer .v3-btn-primary").HasAttribute("disabled"));
        page.Find(".oc-reason").Input("Slip was for another day");
        Assert.True(page.Find(".sd-drawer .v3-btn-primary").HasAttribute("disabled"));         // reason without confirmation
        page.Find(".oc-ack input").Change(true);
        page.Find(".sd-drawer .v3-btn-primary").Click();

        page.WaitForAssertion(() => Assert.Contains("Voided", page.Find(".sd-drawer .oc-status").TextContent), Timeout);
        Assert.Equal("Slip was for another day", reason);
        Assert.Contains("Released", page.Find("table.oc-coverage").TextContent);                // history kept, coverage released
        Assert.DoesNotContain("Void this turnover", page.Find(".sd-drawer").TextContent);
        _api.Verify(x => x.VoidAsync(id, It.IsAny<VoidOfficeCashTurnoverRequest>()), Times.Once);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_remittances_link_appears_only_when_the_module_is_available(bool available, bool shown)
    {
        _api.Setup(x => x.IsAvailableAsync()).ReturnsAsync(Result<bool>.Success(available));
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        var link = RenderComponent<OfficeCashLink>();
        link.WaitForAssertion(() => Assert.Equal(shown, link.FindAll("a[href='/remittances/office-cash']").Count == 1), Timeout);
    }

    [Fact]
    public void The_link_stays_hidden_when_no_client_is_registered()
    {
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Assert.Empty(RenderComponent<OfficeCashLink>().FindAll("a"));
    }

    [Fact]
    public void The_link_stays_hidden_when_the_availability_call_fails()
    {
        _api.Setup(x => x.IsAvailableAsync()).ThrowsAsync(new HttpRequestException("down"));
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Assert.Empty(RenderComponent<OfficeCashLink>().FindAll("a"));
    }
}
