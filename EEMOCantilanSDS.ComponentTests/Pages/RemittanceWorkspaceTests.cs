using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Remittance and liquidation (IA-052): money already collected and turned over, apart from physical form custody. The
/// workspace derives what is expected from posted collections, keeps a shortfall visible, never lets the remitted amount
/// exceed it, never treats remaining tickets as pesos, and records nothing that looks like revenue.
/// </summary>
public sealed class RemittanceWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid CollectorId = Guid.NewGuid();
    private static readonly Guid CollectionA = Guid.NewGuid();
    private static readonly Guid CollectionB = Guid.NewGuid();
    private readonly Mock<IRemittancesApiClient> _api = new();

    public RemittanceWorkspaceTests()
    {
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(x => x.GetAllCollectorsAsync()).ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(new[]
        {
            new CollectorListDto(CollectorId, "Ana Reyes", "ana@example.test", "C-001", [], 0m, 0, null, true)
        }));
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(collectors.Object);
        Services.AddSingleton(_api.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _api.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>(),
                It.IsAny<RevenueInstrumentType?>(), It.IsAny<RemittanceStatus?>()))
            .ReturnsAsync(Result<IReadOnlyList<RemittanceRowDto>>.Success([]));
    }

    private static AccountabilityPositionDto Position(decimal collected = 20000m, decimal remitted = 20000m, int onHand = 9275) => new(
        new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), collected, remitted, collected - remitted, 0, 0m,
        [
            new CollectorPositionDto(CollectorId, "Ana Reyes", collected, remitted, collected - remitted, 0, 0m,
            [
                new FormAccountabilityDto(RevenueInstrumentType.CashTicket, 10000, 725, 0, 0, 0, onHand)
            ])
        ]);

    private void ServePosition(AccountabilityPositionDto position) =>
        _api.Setup(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<AccountabilityPositionDto>.Success(position));

    private static RemittanceScopeDto Scope() => new(CollectorId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null,
        [
            new RemittanceCollectionDto(CollectionA, new DateOnly(2026, 9, 3), "CT-000101", RevenueInstrumentType.CashTicket, "Walk-up", 30m,
                [new RemittanceBreakdownDto(Guid.NewGuid(), "Market Fees", 30m)]),
            new RemittanceCollectionDto(CollectionB, new DateOnly(2026, 9, 4), "CT-000102", RevenueInstrumentType.CashTicket, null, 50m,
                [new RemittanceBreakdownDto(Guid.NewGuid(), "Landing/Berthing", 50m)]),
        ], 80m,
        [new RemittanceBreakdownDto(Guid.NewGuid(), "Landing/Berthing", 50m), new RemittanceBreakdownDto(Guid.NewGuid(), "Market Fees", 30m)],
        "CT-000101", "CT-000102");

    [Fact]
    public void Route_IsOfficeOnly_AndTheTicketsOnHandAreACountNeverPesos()
    {
        Assert.Equal("/accountable-forms/remittances",
            Assert.Single(typeof(Remittances).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Equal("SuperAdmin,Admin",
            Assert.Single(typeof(Remittances).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        ServePosition(Position());

        var cut = RenderComponent<Remittances>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Remittance & Liquidation", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Empty(cut.FindAll("main"));
            var summary = cut.Find("dl[aria-label='Cash position']").TextContent;
            Assert.Contains("₱20,000.00", summary);
            Assert.Contains("Unremitted", summary);
            var row = Assert.Single(cut.FindAll("[aria-label='Collector position'] tbody tr"));
            Assert.Contains("725 issued", row.TextContent);
            Assert.Contains("9275 on hand", row.TextContent);
            // Remaining tickets are never presented as an amount of money.
            Assert.DoesNotContain("₱9,275", cut.Markup);
            Assert.Contains("does not wait for them to be used", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void ARemittanceIsDerivedFromCollections_NothingIsRetyped_AndItIsRecordedWithTheSelectionAndOperationId()
    {
        ServePosition(Position(20000m, 19500m));
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(Scope()));
        RecordRemittanceRequest? sent = null;
        _api.Setup(x => x.RecordAsync(It.IsAny<RecordRemittanceRequest>()))
            .Callback<RecordRemittanceRequest>(r => sent = r)
            .ReturnsAsync(Result<RemittanceDetailDto>.Success(Detail(80m, 70m)));

        var cut = RenderComponent<Remittances>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "New remittance"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New remittance").Click();
        var form = "form[aria-label='New remittance']";
        cut.Find($"{form} .rm-collector-picks input").Change(true);
        cut.FindAll($"{form} button").Single(b => b.TextContent.Trim() == "Show collections").Click();

        cut.WaitForAssertion(() =>
        {
            var breakdown = cut.Find("[aria-label='Collection breakdown']").TextContent;
            Assert.Contains("Market Fees", breakdown);
            Assert.Contains("Landing/Berthing", breakdown);
            Assert.Contains("₱80.00", breakdown);
            Assert.Contains("Nothing here is retyped", cut.Markup);
        }, Timeout);

        // A shortfall stays visible as a difference and is flagged for review; an excess is called out.
        cut.Find($"{form} input[type='number']").Change("70");
        cut.WaitForAssertion(() => Assert.Contains("Difference ₱10.00", cut.Find("p.rm-diff").TextContent), Timeout);
        cut.Find($"{form} input[type='number']").Change("90");
        cut.WaitForAssertion(() => Assert.Contains("cannot exceed", cut.Find("p.rm-diff").TextContent), Timeout);
        cut.Find($"{form} input[type='number']").Change("70");
        cut.Find(form).Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((CollectorId, 70m), (sent!.CollectorId, sent.AmountRemitted));
            Assert.NotEqual(Guid.Empty, sent.ClientOperationId);
            Assert.Equal(new[] { CollectionA, CollectionB }.Order(), sent.CollectionIds!.Order());
            Assert.Contains("for review", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void HistoryListsRemittances_WithTheirDifference_AndLinksToTheirDetail()
    {
        ServePosition(Position());
        var id = Guid.NewGuid();
        _api.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>(),
                It.IsAny<RevenueInstrumentType?>(), It.IsAny<RemittanceStatus?>()))
            .ReturnsAsync(Result<IReadOnlyList<RemittanceRowDto>>.Success(new[]
            {
                new RemittanceRowDto(id, new DateOnly(2026, 9, 30), CollectorId, "Ana Reyes", RevenueInstrumentType.CashTicket, 3,
                    110m, 100m, 10m, "ACK-1", RemittanceStatus.Recorded, DateTime.UtcNow)
            }));

        var cut = RenderComponent<Remittances>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='Remittance history'] tbody tr"));
            Assert.Contains("₱10.00", row.TextContent);
            Assert.Contains("Needs review", row.TextContent);
            Assert.Equal($"/accountable-forms/remittances/{id}", row.QuerySelector("a")!.GetAttribute("href"));
        }, Timeout);
    }

    [Fact]
    public void FailedLoad_SaysSo_InsteadOfShowingAZeroPosition()
    {
        _api.Setup(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<AccountabilityPositionDto>.Failure("offline"));

        var cut = RenderComponent<Remittances>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.Empty(cut.FindAll("dl[aria-label='Cash position']"));
        }, Timeout);
    }

    [Fact]
    public void SeveralCollectors_EachGetTheirOwnSectionAmountAndReference_AndAreRecordedAsOneBatch()
    {
        var benId = Guid.NewGuid();
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(x => x.GetAllCollectorsAsync()).ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(new[]
        {
            new CollectorListDto(CollectorId, "Ana Reyes", "ana@example.test", "C-001", [], 0m, 0, null, true),
            new CollectorListDto(benId, "Ben Cruz", "ben@example.test", "C-002", [], 0m, 0, null, true),
        }));
        Services.AddSingleton(collectors.Object);
        ServePosition(Position());
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(Scope()));
        var benCollection = Guid.NewGuid();
        _api.Setup(x => x.GetScopeAsync(benId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(new RemittanceScopeDto(benId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null,
                [new RemittanceCollectionDto(benCollection, new DateOnly(2026, 9, 5), "CT-000201", RevenueInstrumentType.CashTicket, null, 40m,
                    [new RemittanceBreakdownDto(Guid.NewGuid(), "Market Fees", 40m)])],
                40m, [new RemittanceBreakdownDto(Guid.NewGuid(), "Market Fees", 40m)], "CT-000201", "CT-000201")));
        RecordRemittanceBatchRequest? sent = null;
        _api.Setup(x => x.RecordBatchAsync(It.IsAny<RecordRemittanceBatchRequest>()))
            .Callback<RecordRemittanceBatchRequest>(r => sent = r)
            .ReturnsAsync(Result<IReadOnlyList<RemittanceDetailDto>>.Success([Detail(80m, 80m), Detail(40m, 35m)]));

        var cut = RenderComponent<Remittances>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "New remittance"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New remittance").Click();
        var form = "form[aria-label='New remittance']";
        cut.FindAll($"{form} .rm-collector-picks input")[0].Change(true);
        cut.FindAll($"{form} .rm-collector-picks input")[1].Change(true);
        cut.FindAll($"{form} button").Single(b => b.TextContent.Trim() == "Show collections").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("section.rm-draft").Count), Timeout);
        Assert.Contains("Ana Reyes", cut.FindAll("section.rm-draft")[0].TextContent);
        Assert.Contains("Ben Cruz", cut.FindAll("section.rm-draft")[1].TextContent);
        cut.FindAll("section.rm-draft")[1].QuerySelector("input[type='number']")!.Change("35");
        cut.FindAll("section.rm-draft")[1].QuerySelectorAll(".rm-form-row input")[1].Change("ACK-B");
        var total = cut.Find("dl[aria-label='Batch total']").TextContent;
        Assert.Contains("₱120.00", total);
        Assert.Contains("₱115.00", total);
        Assert.Contains("₱5.00", total);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Record 2 remittances").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal(2, sent!.Remittances.Count);
            var ana = sent.Remittances.Single(x => x.CollectorId == CollectorId);
            var ben = sent.Remittances.Single(x => x.CollectorId == benId);
            Assert.Equal((80m, (string?)null), (ana.AmountRemitted, ana.Reference));
            Assert.Equal((35m, (string?)"ACK-B"), (ben.AmountRemitted, ben.Reference));
            Assert.Equal(new[] { benCollection }, ben.CollectionIds);
            Assert.NotEqual(ana.ClientOperationId, ben.ClientOperationId);
            Assert.Contains("2 remittances recorded, one per collector", cut.Markup);
        }, Timeout);
        _api.Verify(x => x.RecordAsync(It.IsAny<RecordRemittanceRequest>()), Times.Never);
    }

    private static RemittanceDetailDto Detail(decimal expected, decimal remitted) => new(
        new RemittanceRowDto(Guid.NewGuid(), new DateOnly(2026, 9, 30), CollectorId, "Ana Reyes", RevenueInstrumentType.CashTicket, 2,
            expected, remitted, expected - remitted, "ACK-1", RemittanceStatus.Recorded, DateTime.UtcNow),
        new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, "office", null, null, null,
        Scope().Collections, Scope().Breakdown, "CT-000101", "CT-000102", expected != remitted);

    [Fact]
    public void Detail_ShowsTheDifferenceAndCoverage_AndAVoidNeedsAReason()
    {
        var detail = Detail(80m, 70m);
        _api.Setup(x => x.GetDetailAsync(detail.Row.Id)).ReturnsAsync(Result<RemittanceDetailDto>.Success(detail));
        VoidRemittanceRequest? sent = null;
        _api.Setup(x => x.VoidAsync(detail.Row.Id, It.IsAny<VoidRemittanceRequest>()))
            .Callback<Guid, VoidRemittanceRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<RemittanceDetailDto>.Success(detail with { Row = detail.Row with { Status = RemittanceStatus.Voided }, VoidReason = "Counted short" }));

        var cut = RenderComponent<RemittanceDetail>(p => p.Add(x => x.Id, detail.Row.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("₱10.00", cut.Find("dl[aria-label='Remittance']").TextContent);
            Assert.Contains("CT-000101 to CT-000102", cut.Markup);
            Assert.Equal(2, cut.FindAll("[aria-label='Collections covered'] tbody tr").Count);
            Assert.True(cut.Find("form[aria-label='Void remittance'] button").HasAttribute("disabled"));
        }, Timeout);

        cut.Find("form[aria-label='Void remittance'] input").Change("Counted short");
        cut.Find("form[aria-label='Void remittance']").Submit();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Counted short", sent!.Reason);
            Assert.Empty(cut.FindAll("form[aria-label='Void remittance']"));
        }, Timeout);
    }

    [Fact]
    public void AccountableForms_PointsToRemittance_AndNoLongerSaysItIsNotManaged()
    {
        var forms = new Mock<IWcfCollectionsApiClient>();
        forms.Setup(x => x.GetBooksAsync()).ReturnsAsync(Result<IReadOnlyList<AccountableFormBookDto>>.Success([]));
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(x => x.GetAllCollectorsAsync()).ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success([]));
        Services.AddSingleton(forms.Object);
        Services.AddSingleton(collectors.Object);

        var cut = RenderComponent<AccountableForms>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/accountable-forms/remittances");
            Assert.DoesNotContain("Remittance, void/replacement and signed RCD processes are not managed here yet", cut.Markup);
            Assert.Contains("never decides what has been remitted", cut.Markup);
        }, Timeout);
    }
}
