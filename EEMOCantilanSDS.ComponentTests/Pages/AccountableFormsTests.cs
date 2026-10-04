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

        OpenSpecificSerials(cut);
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

        OpenSpecificSerials(cut);
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
    public void AccountWithoutTheCollectorList_CannotAssign_AndIsToldWhy()
    {
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(x => x.GetAllCollectorsAsync())
            .ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Failure("Forbidden", ResultStatus.Forbidden));
        Services.AddSingleton(FormsApi().Object);
        Services.AddSingleton(collectors.Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);

        OpenSpecificSerials(cut);
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
        OpenSpecificSerials(cut);
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
        OpenSpecificSerials(cut);
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

    [Fact]
    public void AutoAllocate_PreviewsTheServerPlan_ThenConfirmsExactlyThatPlan()
    {
        var api = FormsApi();
        var plan = new AutoAllocatePlanDto(BookId, RevenueInstrumentType.CashTicket, 3, 0,
        [
            new AutoAllocatePlanLine(CollectorId, "Ana Reyes", 2, [new SerialRangeDto(103, 104, "CT000103", "CT000104")]),
            new AutoAllocatePlanLine(SecondCollectorId, "Ben Cruz", 1, [new SerialRangeDto(105, 105, "CT000105", "CT000105")])
        ], false);
        api.Setup(x => x.AutoAllocateAsync(It.Is<AutoAllocateFormsRequest>(r => !r.Commit)))
            .ReturnsAsync(Result<AutoAllocatePlanDto>.Success(plan));
        api.Setup(x => x.AutoAllocateAsync(It.Is<AutoAllocateFormsRequest>(r => r.Commit)))
            .ReturnsAsync(Result<AutoAllocatePlanDto>.Success(plan with { Committed = true }));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi(withSecond: true).Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assign / Allocate").Click();

        // Custody first, then collectors; no serial arithmetic is asked for.
        Assert.Contains("In office", cut.Find("dl.af-custody").TextContent);
        cut.FindAll(".af-auto-collectors input[type='checkbox']")[0].Change(true);
        cut.FindAll(".af-auto-collectors input[type='checkbox']")[1].Change(true);
        cut.Find("#af-assign-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("CT000103 – CT000104", cut.Find("table.af-plan").TextContent), Timeout);
        cut.Find("#af-assign-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Allocated 3 Cash Tickets to 2 collectors", cut.Markup), Timeout);
        api.Verify(x => x.AutoAllocateAsync(It.Is<AutoAllocateFormsRequest>(r =>
            r.Commit && r.ExpectedPlan == plan.Lines && r.Shares.All(s => s.Quantity == null) && r.Shares.Count == 2)), Times.Once);
        api.Verify(x => x.AssignBatchAsync(It.IsAny<AssignAccountableFormBatchRequest>()), Times.Never);
    }

    [Fact]
    public void WithNothingInOffice_TheDrawerSaysSo_AndOffersTransferInsteadOfAnAssignmentForm()
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        var held = new AccountableFormBookDto(BookId, RevenueInstrumentType.CashTicket, "Book CT-1", "CT", 101, 103,
        [
            new CashTicketDocumentDto(Guid.NewGuid(), "CT000101", AccountableDocumentState.Consumed, null, 101),
            new CashTicketDocumentDto(Guid.NewGuid(), "CT000102", AccountableDocumentState.Assigned, CollectorId, 102),
            new CashTicketDocumentDto(Guid.NewGuid(), "CT000103", AccountableDocumentState.Assigned, CollectorId, 103),
        ]);
        api.Setup(x => x.GetBooksAsync()).ReturnsAsync(Result<IReadOnlyList<AccountableFormBookDto>>.Success([held]));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi(withSecond: true).Object);

        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assign / Allocate").Click();

        var dialog = cut.Find("[role='dialog']");
        Assert.Contains("No unassigned Cash Tickets are currently in office.", dialog.TextContent);
        Assert.Empty(dialog.QuerySelectorAll("input[type='number']"));
        Assert.DoesNotContain(dialog.QuerySelectorAll("button"), b => b.TextContent.Trim() is "Preview allocation" or "Assign Cash Tickets");
        dialog.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Transfer unused tickets").Click();
        Assert.Contains("Transfer Cash Tickets", cut.Find("[role='dialog'] h2").TextContent);
    }

    // ── AF No. 51: printed serials, cancellation, loss by copy, follow-up references, history ──
    private static readonly Guid SuffixBookId = Guid.NewGuid();

    private static AccountableFormBookDto SuffixBook() => new(
        SuffixBookId, RevenueInstrumentType.OfficialReceipt, "AF No. 51", string.Empty, 2315601, 2315605,
        [
            new(Guid.NewGuid(), "2315601 A", AccountableDocumentState.InOffice, null, 2315601),
            new(Guid.NewGuid(), "2315602 A", AccountableDocumentState.InOffice, null, 2315602),
            new(Guid.NewGuid(), "2315603 A", AccountableDocumentState.Assigned, CollectorId, 2315603),
            new(Guid.NewGuid(), "2315604 A", AccountableDocumentState.Voided, null, 2315604),
            new(Guid.NewGuid(), "2315605 A", AccountableDocumentState.Lost, null, 2315605),
        ], NumberSuffix: " A", Quantity: 5, SourceAuthority: "Municipal Treasurer");

    private Mock<IWcfCollectionsApiClient> SuffixApi(params AccountableFormExceptionDto[] exceptions)
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetBooksAsync()).ReturnsAsync(Result<IReadOnlyList<AccountableFormBookDto>>.Success([SuffixBook()]));
        api.Setup(x => x.GetFormExceptionsAsync()).ReturnsAsync(Result<IReadOnlyList<AccountableFormExceptionDto>>.Success(exceptions));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(CollectorsApi().Object);
        return api;
    }

    private IRenderedComponent<AccountableForms> OpenOfficialReceipts()
    {
        var cut = RenderComponent<AccountableForms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Official Receipts").Click();
        return cut;
    }

    [Fact]
    public void RegisteringReceipts_TakesTheSerialsAsPrinted_CountsTheQuantity_AndSubmitsThemExactly()
    {
        var api = SuffixApi();
        api.Setup(x => x.RegisterFormsAsync(It.IsAny<RegisterAccountableFormsRequest>()))
            .ReturnsAsync(Result<AccountableFormBookDto>.Success(SuffixBook()));
        var cut = OpenOfficialReceipts();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register received book").Click();

        cut.Find("input[placeholder='2315601 A']").Input("2315601 A");
        cut.Find("input[placeholder='2315650 A']").Input("2315650 A");

        Assert.Contains("2315601 A – 2315650 A · 50 receipts", cut.Find("[role='dialog']").TextContent);
        cut.Find("#af-register-form").Submit();

        cut.WaitForAssertion(() => api.Verify(x => x.RegisterFormsAsync(It.Is<RegisterAccountableFormsRequest>(r =>
            r.InstrumentType == RevenueInstrumentType.OfficialReceipt && r.FirstSerial == "2315601 A" && r.LastSerial == "2315650 A"
            && r.Quantity == null && r.FormVariant == null && r.SourceAuthority == "Municipal Treasurer")), Times.Once), Timeout);
        // Official Receipts no longer go through the numeric prefix-and-digits route.
        api.Verify(x => x.ReceiveBookAsync(It.IsAny<ReceiveAccountableFormBookRequest>()), Times.Never);
    }

    [Fact]
    public void ARangeWithADifferentSuffix_OrAWrongQuantity_IsExplainedAndCannotBeSubmitted()
    {
        var api = SuffixApi();
        var cut = OpenOfficialReceipts();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Register received book").Click();

        cut.Find("input[placeholder='2315601 A']").Input("2315601 A");
        cut.Find("input[placeholder='2315650 A']").Input("2315650 B");
        Assert.Contains("same printed prefix and suffix", cut.Find("[role='alert']").TextContent);
        Assert.True(cut.Find("button[form='af-register-form']").HasAttribute("disabled"));

        cut.Find("input[placeholder='2315650 A']").Input("2315650 A");
        cut.Find("input[type='number']").Input("40");
        Assert.Contains("That range holds 50, not 40", cut.Find("[role='alert']").TextContent);
        Assert.True(cut.Find("button[form='af-register-form']").HasAttribute("disabled"));
        api.Verify(x => x.RegisterFormsAsync(It.IsAny<RegisterAccountableFormsRequest>()), Times.Never);
    }

    [Fact]
    public void Receipts_ShowTheirPrintedSerialAndReadableStates_WithNoInternalCodes()
    {
        SuffixApi();
        var cut = OpenOfficialReceipts();

        var table = cut.Find("[aria-label='Official Receipt units']").TextContent;
        Assert.Contains("2315601 A", table);
        Assert.Contains("Cancelled", table);
        Assert.Contains("Lost", table);
        Assert.DoesNotContain("Voided", cut.Find(".af-page").TextContent);
        Assert.DoesNotMatch("[A-Z]+_[A-Z]+", cut.Markup);
        Assert.Contains("Lost", cut.Find("dl[aria-label='Official Receipt custody']").TextContent);
    }

    [Fact]
    public void ReportingALoss_ByCopy_NormalizesThePrintedSerial_AndSendsOnlyTheMissingCopy()
    {
        var api = SuffixApi();
        api.Setup(x => x.ReportFormLossAsync(It.IsAny<ReportFormLossRequest>()))
            .ReturnsAsync(Result<FormLossResultDto>.Success(new(1, 1, "2315601 A", "2315601 A")));
        var cut = OpenOfficialReceipts();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Report lost form").Click();

        // The serial may be typed without the space; it is matched to the registered form, which keeps its printed value.
        cut.Find("#af-loss-form input[required]").Input("2315601A");
        cut.Find("input[name='af-loss-scope'][type='radio']:not([checked])").Change(true);
        cut.FindAll("#af-loss-form input[type='checkbox']").Single(c => c.ParentElement!.TextContent.Contains("Duplicate")).Change(true);
        cut.Find("#af-loss-form textarea").Input("Duplicate was not in the booklet");
        Assert.Contains("Will block", cut.Find("[role='dialog']").TextContent);
        cut.Find("#af-loss-form").Submit();

        cut.WaitForAssertion(() => api.Verify(x => x.ReportFormLossAsync(It.Is<ReportFormLossRequest>(r =>
            r.FormBookId == SuffixBookId && r.FirstSerialNumber == 2315601 && r.LastSerialNumber == 2315601
            && r.Copies == AccountableFormCopies.Duplicate && r.Narrative == "Duplicate was not in the booklet")), Times.Once), Timeout);
        cut.WaitForAssertion(() => Assert.Contains("blocked from use", cut.Markup), Timeout);
    }

    [Fact]
    public void ALossWithoutAnyChosenCopy_IsRefusedBeforeSubmitting()
    {
        var api = SuffixApi();
        var cut = OpenOfficialReceipts();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Report lost form").Click();
        cut.Find("#af-loss-form input[required]").Input("2315601 A");
        cut.Find("input[name='af-loss-scope'][type='radio']:not([checked])").Change(true);
        cut.Find("#af-loss-form textarea").Input("Missing");

        Assert.Contains("Choose what is missing", cut.Find("[role='dialog'] [role='alert']").TextContent);
        Assert.True(cut.Find("button[form='af-loss-form']").HasAttribute("disabled"));
        api.Verify(x => x.ReportFormLossAsync(It.IsAny<ReportFormLossRequest>()), Times.Never);
    }

    [Fact]
    public void Cancelling_ARegisteredReceipt_NeedsOnlyItsSerialAndReason_AndPromisesNoMoney()
    {
        SuffixApi();
        var remittances = new Mock<IRemittancesApiClient>();
        remittances.Setup(x => x.SpoilAsync(It.IsAny<SpoilFormRequest>())).ReturnsAsync(Result<SpoiledFormDto>.Success(
            new(Guid.NewGuid(), "2315602 A", RevenueInstrumentType.OfficialReceipt, "Wrong payor", null, "Office", DateTime.UtcNow, null)));
        Services.AddSingleton(remittances.Object);
        var cut = OpenOfficialReceipts();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Record cancellation").Click();

        cut.Find("#af-spoil-form input[required]").Input("2315602 A");
        cut.FindAll("#af-spoil-form input[required]")[1].Change("Wrong payor");
        cut.Find("#af-spoil-form").Submit();

        cut.WaitForAssertion(() => remittances.Verify(x => x.SpoilAsync(It.Is<SpoilFormRequest>(r => r.Reason == "Wrong payor")), Times.Once), Timeout);
        cut.WaitForAssertion(() => Assert.Contains("cancelled. It is not revenue and cannot return to stock. Add the RCD reference", cut.Markup), Timeout);
    }

    [Fact]
    public void Exceptions_ShowNeedsFollowUp_UntilTheReferenceIsAdded_ThenShowTheReference()
    {
        var needs = new AccountableFormExceptionDto(Guid.NewGuid(), "2315604 A", RevenueInstrumentType.OfficialReceipt, "Cancelled",
            "Wrong payor", null, null, "Office", DateTime.UtcNow, null, null, true, []);
        var done = new AccountableFormExceptionDto(Guid.NewGuid(), "2315605 A", RevenueInstrumentType.OfficialReceipt, "Duplicate missing",
            "Duplicate not in the booklet", "Market", new DateOnly(2026, 10, 1), "Office", DateTime.UtcNow, CollectorId, "Ana Reyes", false, ["Notice of Loss 10-2026"]);
        var api = SuffixApi(needs, done);
        api.Setup(x => x.AddFormReferenceAsync(It.IsAny<AddFormReferenceRequest>()))
            .ReturnsAsync(Result<FormReferenceResultDto>.Success(new(1, "2315604 A", "2315604 A")));
        var cut = OpenOfficialReceipts();

        Assert.Contains("Exceptions (1)", cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Exceptions")).TextContent);
        cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Exceptions")).Click();

        var rows = cut.FindAll("[aria-label='Cancelled and lost forms'] tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Needs follow-up", rows[0].TextContent);
        Assert.Contains("Notice of Loss 10-2026", rows[1].TextContent);
        Assert.Contains("Duplicate missing", rows[1].TextContent);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add RCD reference").Click();
        cut.Find("#af-ref-form input[required]").Input("RCD 10-2026");
        cut.Find("#af-ref-form").Submit();

        cut.WaitForAssertion(() => api.Verify(x => x.AddFormReferenceAsync(It.Is<AddFormReferenceRequest>(r =>
            r.FormBookId == SuffixBookId && r.FirstSerialNumber == 2315604 && r.Kind == AccountableFormReferenceKind.Cancellation
            && r.Reference == "RCD 10-2026")), Times.Once), Timeout);
    }

    [Fact]
    public void HistoryAndAccountabilitySupport_ReadTheLedger_AndDoNotClaimToBeTheOfficialReport()
    {
        var api = SuffixApi();
        api.Setup(x => x.GetFormHistoryAsync(RevenueInstrumentType.OfficialReceipt)).ReturnsAsync(
            Result<IReadOnlyList<AccountableFormHistoryEventDto>>.Success(
            [
                new(DateTime.UtcNow, "Assigned", "2315601 A – 2315625 A", 25, "Head", "Office", "Bobby Mercado", null),
                new(DateTime.UtcNow.AddHours(-1), "Duplicate missing", "2315603 A", 1, "Head", "Bobby Mercado", null, "Market · Not in the booklet"),
            ]));
        api.Setup(x => x.GetFormRaafSupportAsync(RevenueInstrumentType.OfficialReceipt, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(
            Result<AccountableFormRaafSupportDto>.Success(new(RevenueInstrumentType.OfficialReceipt, 2026, 10,
            [
                new(SuffixBookId, "AF No. 51", 0, 5, 1, 1, 1, 2, [], [], [], [], [], [new(2315601, 2315602, "2315601 A", "2315602 A")]),
            ])));
        var cut = OpenOfficialReceipts();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "History").Click();
        var history = cut.Find("[aria-label='Official Receipt history']").TextContent;
        Assert.Contains("2315601 A – 2315625 A", history);
        Assert.Contains("Office → Bobby Mercado", history);
        Assert.Contains("Duplicate missing", history);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Accountability").Click();
        var support = cut.Find("[aria-label='Accountability support by book']").TextContent;
        Assert.Contains("2315601 A – 2315602 A", support);
        Assert.Contains("it is not that report", cut.Markup);
    }

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

    /// <summary>Opens Assign / Allocate on the page's only book and switches to the advanced specific-serials path.</summary>
    private static void OpenSpecificSerials(IRenderedComponent<AccountableForms> cut)
    {
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Assign / Allocate").Click();
        cut.FindAll("[role='dialog'] button").Single(b => b.TextContent.Trim() == "Specific serials").Click();
    }
}
