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

        // The range starts at the first ticket still in office (serial 103).
        cut.Find("[role='dialog']").QuerySelectorAll("input")[1].Change("105");
        Assert.Contains("3 Cash Tickets will be assigned to Ana Reyes.", cut.Find("[role='dialog']").TextContent);

        cut.Find("#af-assign-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Assigned 3 Cash Tickets to Ana Reyes", cut.Markup), Timeout);
        api.Verify(x => x.AssignCashTicketsAsync(It.Is<AssignAccountableFormRangeRequest>(r =>
            r.FormBookId == BookId && r.AssignedUserId == CollectorId && r.FirstSerialNumber == 103 && r.LastSerialNumber == 105)), Times.Once);
        api.Verify(x => x.PostAsync(It.IsAny<WcfCollectionPostRequest>()), Times.Never);
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

    private static Mock<IWcfCollectionsApiClient> FormsApi()
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetBooksAsync())
            .ReturnsAsync(Result<IReadOnlyList<AccountableFormBookDto>>.Success(new[] { Book() }));
        return api;
    }

    private static Mock<ICollectorsApiClient> CollectorsApi()
    {
        var api = new Mock<ICollectorsApiClient>();
        api.Setup(x => x.GetAllCollectorsAsync()).ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(new[]
        {
            new CollectorListDto(CollectorId, "Ana Reyes", "ana@example.test", "C-001", [], 0m, 0, null, true),
        }));
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
