using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Accountable Forms is Head/Admin physical custody of Cash Tickets. It registers received books and assigns ranges to
/// collectors through the existing custody endpoints; it never records money.
/// </summary>
public sealed class AccountableFormsTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid CollectorId = Guid.NewGuid();

    public AccountableFormsTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IRemittancesApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Route_IsAccountableForms_ForHeadAndAdmin()
    {
        var route = Assert.Single(typeof(AccountableForms).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>());
        var authorize = Assert.Single(typeof(AccountableForms).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal("/accountable-forms", route.Template);
        Assert.Equal("SuperAdmin,Admin", authorize.Roles);
    }

    [Fact]
    public void Register_ShowsBookCounts_AndCollectorCustodyAsSerialRuns()
    {
        Services.AddSingleton(FormsApi().Object);
        Services.AddSingleton(CollectorsApi().Object);

        var cut = RenderComponent<AccountableForms>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Accountable Forms", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Empty(cut.FindAll("main"));

            var bookRow = Assert.Single(cut.FindAll("[aria-label='Registered Cash Ticket books'] tbody tr"));
            Assert.Contains("Book A", bookRow.TextContent);
            Assert.Contains("CT000101 – CT000106", bookRow.TextContent);

            var custody = Assert.Single(cut.FindAll("[aria-label='Cash Tickets held by collectors'] tbody tr"));
            Assert.Contains("Ana Reyes", custody.TextContent);
            Assert.Contains("CT000101 – CT000102", custody.TextContent);

            Assert.Contains("Needs review", cut.Find("dl[aria-label='Cash Ticket custody']").TextContent);
        }, Timeout);
    }

    [Fact]
    public void RegisterReceivedBook_SubmitsTheCashTicketRange_AndRefreshes()
    {
        var api = FormsApi();
        api.Setup(x => x.ReceiveBookAsync(It.IsAny<ReceiveAccountableFormBookRequest>()))
            .ReturnsAsync(Result<AccountableFormBookDto>.Success(Book()));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi().Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register received book").Click();
        var inputs = cut.Find("[role='dialog']").QuerySelectorAll("input");
        inputs[0].Change("Book B");
        cut.Find("[role='dialog']").QuerySelectorAll("input")[2].Change("201");
        cut.Find("[role='dialog']").QuerySelectorAll("input")[3].Change("300");

        Assert.Contains("CT000201 – CT000300 · 100 tickets", cut.Find("[role='dialog']").TextContent);
        cut.Find("#af-register-form").Submit();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")), Timeout);
        api.Verify(x => x.ReceiveBookAsync(It.Is<ReceiveAccountableFormBookRequest>(r =>
            r.InstrumentType == RevenueInstrumentType.CashTicket && r.SeriesName == "Book B"
            && r.FirstSerialNumber == 201 && r.LastSerialNumber == 300 && r.SerialWidth == 6)), Times.Once);
        api.Verify(x => x.GetBooksAsync(), Times.Exactly(2));
    }

    [Fact]
    public void AssignCashTickets_PreviewsTheTransfer_AndSubmitsCustodyOnly()
    {
        var api = FormsApi();
        api.Setup(x => x.AssignCashTicketsAsync(It.IsAny<AssignAccountableFormRangeRequest>()))
            .ReturnsAsync(Result<int>.Success(3));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi().Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assign Cash Tickets").Click();
        var selects = cut.Find("[role='dialog']").QuerySelectorAll("select");
        selects[0].Change(BookId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("select")[1].Change(CollectorId.ToString());

        // The range starts at the first ticket still in office (serial 103); the office states a quantity, not serials.
        cut.Find("[role='dialog']").QuerySelectorAll("input")[1].Change("3");
        var availability = cut.Find("dl[aria-label='Availability of the requested range']").TextContent;
        Assert.Contains("3 · CT000103 – CT000105", availability);
        Assert.Contains("Remaining in office after this", availability);

        cut.Find("#af-assign-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Assigned 3 Cash Tickets to Ana Reyes (CT000103 – CT000105)", cut.Markup), Timeout);
        api.Verify(x => x.AssignCashTicketsAsync(It.Is<AssignAccountableFormRangeRequest>(r =>
            r.FormBookId == BookId && r.AssignedUserId == CollectorId && r.FirstSerialNumber == 103 && r.LastSerialNumber == 105)), Times.Once);
        api.Verify(x => x.PostAsync(It.IsAny<WcfCollectionPostRequest>()), Times.Never);
    }

    [Fact]
    public void OfficialReceipts_AreShownApartFromCashTickets_WithTheirOwnCustody()
    {
        Services.AddSingleton(FormsApi(withOrBook: true).Object);
        Services.AddSingleton(CollectorsApi().Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);

        // Default view is Cash Tickets and never lists OR units.
        Assert.DoesNotContain("OR000201", cut.Markup);
        Assert.Single(cut.FindAll("[aria-label='Registered Cash Ticket books'] tbody tr"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Official Receipts").Click();

        var books = Assert.Single(cut.FindAll("[aria-label='Registered Official Receipt books'] tbody tr"));
        Assert.Contains("Book OR-1", books.TextContent);
        Assert.DoesNotContain("CT000101", cut.Markup);
        var holding = Assert.Single(cut.FindAll("[aria-label='Official Receipts held by collectors'] tbody tr"));
        Assert.Contains("Ana Reyes", holding.TextContent);
        Assert.Contains("OR000201", holding.TextContent);
        Assert.Equal("true", cut.FindAll("button").Single(b => b.TextContent.Trim() == "Official Receipts").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void AssignOfficialReceipts_UsesTheOrRoute_AndNeverTheCashTicketRoute()
    {
        var api = FormsApi(withOrBook: true);
        api.Setup(x => x.AssignOfficialReceiptsAsync(It.IsAny<AssignAccountableFormRangeRequest>()))
            .ReturnsAsync(Result<int>.Success(2));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi().Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Official Receipts").Click();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assign Official Receipts").Click();
        cut.Find("[role='dialog']").QuerySelectorAll("select")[0].Change(OrBookId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("select")[1].Change(CollectorId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("input")[1].Change("2");
        Assert.Contains("2 · OR000203 – OR000204", cut.Find("dl[aria-label='Availability of the requested range']").TextContent);

        cut.Find("#af-assign-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Assigned 2 Official Receipts to Ana Reyes", cut.Markup), Timeout);
        api.Verify(x => x.AssignOfficialReceiptsAsync(It.Is<AssignAccountableFormRangeRequest>(r =>
            r.FormBookId == OrBookId && r.AssignedUserId == CollectorId && r.FirstSerialNumber == 203 && r.LastSerialNumber == 204)), Times.Once);
        api.Verify(x => x.AssignCashTicketsAsync(It.IsAny<AssignAccountableFormRangeRequest>()), Times.Never);
    }

    [Fact]
    public void RegisterOfficialReceiptBook_SubmitsTheOrInstrument_AndAllowsANumericOnlyBook()
    {
        var api = FormsApi(withOrBook: true);
        api.Setup(x => x.ReceiveBookAsync(It.IsAny<ReceiveAccountableFormBookRequest>()))
            .ReturnsAsync(Result<AccountableFormBookDto>.Success(OrBook()));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi().Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Official Receipts").Click();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register received book").Click();

        var inputs = cut.Find("[role='dialog']").QuerySelectorAll("input");
        inputs[0].Change("Book OR-2");
        Assert.Equal(string.Empty, inputs[1].GetAttribute("value") ?? string.Empty); // prefix stays blank
        cut.Find("[role='dialog']").QuerySelectorAll("input")[2].Change("501");
        cut.Find("[role='dialog']").QuerySelectorAll("input")[3].Change("550");
        Assert.Contains("000501 – 000550 · 50 receipts", cut.Find("[role='dialog']").TextContent);
        cut.Find("#af-register-form").Submit();

        cut.WaitForAssertion(() => api.Verify(x => x.ReceiveBookAsync(It.Is<ReceiveAccountableFormBookRequest>(r =>
            r.InstrumentType == RevenueInstrumentType.OfficialReceipt && r.NumberPrefix == string.Empty
            && r.FirstSerialNumber == 501 && r.LastSerialNumber == 550)), Times.Once), Timeout);
    }

    [Fact]
    public void AccountWithoutTheCollectorList_CannotAssign_AndIsToldWhy()
    {
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(x => x.GetAllCollectorsAsync())
            .ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Failure("Forbidden", ResultStatus.Forbidden));
        Services.AddSingleton(FormsApi().Object);
        Services.AddSingleton(collectors.Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assign Cash Tickets").Click();
        var dialog = cut.Find("[role='dialog']");

        Assert.Contains("isn't available to this account", dialog.TextContent);
        Assert.True(dialog.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Assign Cash Tickets").HasAttribute("disabled"));
    }

    [Fact]
    public void ARangeHeldByAnotherCollector_IsExplainedAsAvailability_AndCannotBeSubmitted()
    {
        var api = FormsApi();
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi(withSecond: true).Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assign Cash Tickets").Click();
        cut.Find("[role='dialog']").QuerySelectorAll("select")[0].Change(BookId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("select")[1].Change(SecondCollectorId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("input")[0].Change("101");
        cut.Find("[role='dialog']").QuerySelectorAll("input")[1].Change("2");

        var dialog = cut.Find("[role='dialog']");
        var availability = cut.Find("dl[aria-label='Availability of the requested range']").TextContent;
        Assert.Contains("Already assigned · Ana Reyes", availability);
        Assert.Contains("No Cash Tickets are available to assign from this range. Return or transfer unused tickets before assigning them to another collector.",
            dialog.TextContent);
        Assert.DoesNotContain("not currently in office", dialog.TextContent);
        Assert.True(dialog.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Assign Cash Tickets").HasAttribute("disabled"));
        api.Verify(x => x.AssignCashTicketsAsync(It.IsAny<AssignAccountableFormRangeRequest>()), Times.Never);
    }

    [Fact]
    public void SeveralCollectors_GetContiguousRanges_SubmittedAsOneBatch()
    {
        var api = FormsApi();
        api.Setup(x => x.AssignBatchAsync(It.IsAny<AssignAccountableFormBatchRequest>())).ReturnsAsync(Result<int>.Success(3));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi(withSecond: true).Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assign Cash Tickets").Click();
        cut.Find("[role='dialog']").QuerySelectorAll("select")[0].Change(BookId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("select")[1].Change(CollectorId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("input")[1].Change("1");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add collector").Click();
        cut.Find("[role='dialog']").QuerySelectorAll("select")[2].Change(SecondCollectorId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("input")[2].Change("2");

        var rows = cut.FindAll(".af-batch-row");
        Assert.Contains("CT000103", rows[0].TextContent);
        Assert.Contains("CT000104 – CT000105", rows[1].TextContent);
        Assert.Contains("3 Cash Tickets to 2 collectors", cut.Find(".v3-modal-footer").TextContent);
        cut.Find("#af-assign-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Assigned 3 Cash Tickets to 2 collectors.", cut.Markup), Timeout);
        api.Verify(x => x.AssignBatchAsync(It.Is<AssignAccountableFormBatchRequest>(r =>
            r.FormBookId == BookId && r.InstrumentType == RevenueInstrumentType.CashTicket && r.Lines.Count == 2
            && r.Lines[0] == new AccountableFormBatchLine(CollectorId, 103, 103)
            && r.Lines[1] == new AccountableFormBatchLine(SecondCollectorId, 104, 105))), Times.Once);
        api.Verify(x => x.AssignCashTicketsAsync(It.IsAny<AssignAccountableFormRangeRequest>()), Times.Never);
    }

    [Fact]
    public void Transfer_MovesUnusedTicketsWithAReason_AndRefusesARangeWithAnIssuedTicket()
    {
        var api = FormsApi();
        api.Setup(x => x.TransferAsync(It.IsAny<TransferAccountableFormsRequest>())).ReturnsAsync(Result<int>.Success(2));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi(withSecond: true).Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Transfer").Click();
        cut.Find("[role='dialog']").QuerySelectorAll("select")[0].Change(BookId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("input")[0].Change("101");
        cut.Find("[role='dialog']").QuerySelectorAll("input")[1].Change("106");
        Assert.Contains("CT000103 – CT000106 cannot move (in office, consumed)", cut.Find("[role='dialog']").TextContent);

        cut.Find("[role='dialog']").QuerySelectorAll("input")[1].Change("102");
        Assert.Contains("Held by Ana Reyes · 2 unused", cut.Find("[role='dialog']").TextContent);
        cut.Find("[role='dialog']").QuerySelectorAll("select")[1].Change(SecondCollectorId.ToString());
        cut.Find("[role='dialog']").QuerySelectorAll("input")[2].Input("Ana on leave");
        cut.Find("#af-transfer-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Transferred 2 unused Cash Tickets (CT000101 – CT000102) to Ben Cruz.", cut.Markup), Timeout);
        api.Verify(x => x.TransferAsync(It.Is<TransferAccountableFormsRequest>(r =>
            r.FormBookId == BookId && r.FirstSerialNumber == 101 && r.LastSerialNumber == 102
            && r.ToUserId == SecondCollectorId && r.Reason == "Ana on leave")), Times.Once);
    }

    private static readonly Guid SecondCollectorId = Guid.NewGuid();
    private static readonly Guid OrBookId = Guid.NewGuid();

    private static Mock<IWcfCollectionsApiClient> FormsApi(bool withOrBook = false)
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        var books = withOrBook ? new[] { Book(), OrBook() } : new[] { Book() };
        api.Setup(x => x.GetBooksAsync())
            .ReturnsAsync(Result<IReadOnlyList<AccountableFormBookDto>>.Success(books));
        return api;
    }

    private static AccountableFormBookDto OrBook() => new(
        OrBookId, RevenueInstrumentType.OfficialReceipt, "Book OR-1", "OR", 201, 205,
        [
            OrDoc(201, AccountableDocumentState.Assigned, CollectorId),
            OrDoc(202, AccountableDocumentState.Consumed, CollectorId),
            OrDoc(203, AccountableDocumentState.InOffice),
            OrDoc(204, AccountableDocumentState.InOffice),
            OrDoc(205, AccountableDocumentState.InOffice),
        ]);

    private static CashTicketDocumentDto OrDoc(long serial, AccountableDocumentState state, Guid? assignedTo = null) =>
        new(Guid.NewGuid(), $"OR{serial:000000}", state, assignedTo, serial);

    private static Mock<ICollectorsApiClient> CollectorsApi(bool withSecond = false)
    {
        var api = new Mock<ICollectorsApiClient>();
        var collectors = new List<CollectorListDto>
        {
            new(CollectorId, "Ana Reyes", "ana@example.test", "C-001", [], 0m, 0, null, true),
        };
        if (withSecond) collectors.Add(new(SecondCollectorId, "Ben Cruz", "ben@example.test", "C-002", [], 0m, 0, null, true));
        api.Setup(x => x.GetAllCollectorsAsync()).ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(collectors));
        return api;
    }

    private static AccountableFormBookDto Book() => new(
        BookId, RevenueInstrumentType.CashTicket, "Book A", "CT", 101, 106,
        [
            Doc(101, AccountableDocumentState.Assigned, CollectorId),
            Doc(102, AccountableDocumentState.Assigned, CollectorId),
            Doc(103, AccountableDocumentState.InOffice),
            Doc(104, AccountableDocumentState.InOffice),
            Doc(105, AccountableDocumentState.InOffice),
            Doc(106, AccountableDocumentState.Consumed, CollectorId),
        ]);

    private static CashTicketDocumentDto Doc(long serial, AccountableDocumentState state, Guid? assignedTo = null) =>
        new(Guid.NewGuid(), $"CT{serial:000000}", state, assignedTo, serial);
}
