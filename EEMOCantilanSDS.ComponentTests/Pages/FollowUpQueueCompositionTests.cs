using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Components.Pages.Shared.Actions;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using FollowUpPage = EEMOCantilanSDS.Client.Components.Pages.Reports.FollowUpQueue;

public sealed class FollowUpQueueCompositionTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void LoadsExistingQueueApiAndShowsReasonSeparatelyFromPriority()
    {
        var (cut, api) = RenderQueue();

        cut.WaitForAssertion(() =>
        {
            var lowerAge = RowFor(cut, "Lower-age delinquent");
            Assert.Equal("Delinquent", lowerAge.QuerySelector(".fq-reason")?.TextContent.Trim());
            // A Normal row carries no priority tag; only urgent rows are tagged.
            Assert.Null(lowerAge.QuerySelector(".fq-priority"));
            Assert.Contains("Unpaid · 2 months", lowerAge.TextContent);

            var higherAge = RowFor(cut, "Higher-age delinquent");
            Assert.Equal("Delinquent", higherAge.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.Null(higherAge.QuerySelector(".fq-priority"));                                  // no visible Critical badge
            Assert.Contains("fq-pri-critical", higherAge.ClassName);                                // the row rail still carries the urgency
            Assert.Contains("Unpaid · 4 months", higherAge.TextContent);
        }, RenderTimeout);

        api.Verify(a => a.GetFollowUpQueueAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    private static ObligationFollowUpItemDto SpaceMonth(string payer, DateOnly period, decimal owed, string reason, string? basis = null, DateOnly? termEnded = null, bool earlier = false) =>
        new(Guid.NewGuid(), Guid.Empty, payer, "4", new(FollowUpScopeKind.MonthlySpace, "VEGETABLE_FRUIT_SPACE_RENTAL", "Vegetable / Fruits"),
            period, owed, 0m, owed, "Normal", null, "/operations/vegetable-fruit", reason, basis, earlier, termEnded);

    [Fact]
    public void SpaceRentalMonthsCountUnderTheirOwnReasonChips_SoAllNeverReadsZeroWhileTheSpaceTableListsItems()
    {
        var queue = SampleQueue() with
        {
            ObligationItems =
            [
                SpaceMonth("Greggor Dal", new(2026, 3, 1), 10_000m, "arrears", "Expired contract", new(2025, 10, 24)),
                SpaceMonth("Ana Cruz", new(2026, 8, 1), 500m, "delinquent")
            ]
        };
        var (cut, _) = RenderQueue(queue);
        cut.WaitForAssertion(() =>
        {
            var chips = cut.FindAll(".fq-reasons .fq-chip");
            int Count(string label) => int.Parse(chips.Single(c => c.TextContent.Trim().StartsWith(label)).QuerySelector(".fq-chip-count")!.TextContent.Trim());
            Assert.Equal(9, Count("All"));                                                         // the seven facility rows plus the two space months
            Assert.Contains("Arrears · term ended Oct 24, 2025", cut.Find("#fq-space-title").Closest("section")!.TextContent);
        }, RenderTimeout);

        cut.FindAll(".fq-reasons .fq-chip").Single(c => c.TextContent.Trim().StartsWith("Arrears")).Click();
        var spaces = cut.Find("#fq-space-title").Closest("section")!;
        Assert.Contains("Greggor Dal", spaces.TextContent);
        Assert.DoesNotContain("Ana Cruz", spaces.TextContent);                                     // a Delinquent month is not listed under Arrears
    }

    [Fact]
    public void Missing_optional_OR_is_not_an_action_required_alert_and_priority_badges_are_hidden()
    {
        var (cut, _) = RenderQueue();
        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".fq-priority"));
            Assert.DoesNotContain(cut.FindAll(".fq-row"), r => r.TextContent.Contains("Receipt follow-up"));
            Assert.DoesNotContain("Missing OR", cut.Find(".fq-section").TextContent);
            Assert.Equal(7, cut.FindAll(".fq-row").Count);
            Assert.Equal("7", cut.FindAll(".fq-reasons .fq-chip")[0].QuerySelector(".fq-chip-count")!.TextContent.Trim());
        }, RenderTimeout);
    }

    [Fact]
    public void Online_payment_receipt_issuance_remains_visible_while_optional_blank_or_is_hidden()
    {
        var optional = SampleQueue().Items.Single(x => x.ReasonKind == "missingor");
        var awaitingOnline = optional with
        {
            Person = "Online payment awaiting receipt",
            Section = 1,
            Reason = "Missing OR",
            Priority = "High",
            Action = "Encode OR",
            Link = "/online-payments",
            ReceiptActionRequired = true
        };
        var queue = SampleQueue() with { Items = SampleQueue().Items.Append(awaitingOnline).ToArray() };
        var (cut, _) = RenderQueue(queue);
        cut.WaitForAssertion(() =>
        {
            var onlineRow = RowFor(cut, "Online payment awaiting receipt");
            Assert.Contains("Missing OR", onlineRow.TextContent);
            Assert.Contains("Online payment awaiting receipt", cut.Markup);
            Assert.DoesNotContain(cut.FindAll(".fq-row"), row => row.TextContent.Contains("Receipt follow-up"));
            Assert.Equal(8, cut.FindAll(".fq-row").Count); // one new mandatory row, the legacy row still filtered
        }, RenderTimeout);
    }

    [Fact]
    public void KeepsCurrentPeriodUnpaidAndPartialSeparateFromDelinquency()
    {
        var (cut, _) = RenderQueue();

        cut.WaitForAssertion(() =>
        {
            var unpaid = RowFor(cut, "Current unpaid");
            Assert.Equal("Current-period unpaid", unpaid.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.Contains("Unpaid", unpaid.TextContent);
            Assert.DoesNotContain("Delinquent", unpaid.TextContent);
            Assert.Contains("September 2026", unpaid.TextContent);

            var partial = RowFor(cut, "Current partial");
            Assert.Equal("Partial payment", partial.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.Contains("Partial", partial.TextContent);
            Assert.DoesNotContain("Delinquent", partial.TextContent);
        }, RenderTimeout);
    }

    [Fact]
    public void KeepsSourceReasonWhenStatusMentionsOccupantAndKeepsExpiryFreeOfDebtLanguage()
    {
        var (cut, _) = RenderQueue();

        cut.WaitForAssertion(() =>
        {
            var lapsedTerm = RowFor(cut, "Lapsed-term occupant");
            Assert.Equal("Past occupancy balance", lapsedTerm.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.DoesNotContain("Ended occupancy balance", lapsedTerm.TextContent);
            Assert.Contains("No longer the occupant", lapsedTerm.TextContent);
            Assert.Contains("Jan–Aug 2026", lapsedTerm.TextContent);
            Assert.DoesNotContain("Delinquent", lapsedTerm.TextContent);
            Assert.False(lapsedTerm.TextContent.Contains("Arrears", StringComparison.OrdinalIgnoreCase));

            var expiring = RowFor(cut, "Expiring account");
            Assert.Equal("Contract expiring", expiring.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.Contains("Expiring soon", expiring.TextContent);
            Assert.DoesNotContain("Delinquent", expiring.TextContent);
            Assert.DoesNotContain("₱", expiring.TextContent);
        }, RenderTimeout);
    }

    [Fact]
    public void PreservesInlineActionsAndDoesNotInferArrearsFromAFilterShortcut()
    {
        var (cut, _) = RenderQueue();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Review account", StringComparison.Ordinal));
            Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Review item", StringComparison.Ordinal));
            Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Hide locally", StringComparison.Ordinal));
            Assert.Contains("Past-period view", cut.Markup);
            // The Arrears shortcut exists, but no sample row is Arrears: a row is never labelled Arrears by the page itself.
            Assert.DoesNotContain(cut.FindAll(".fh-row, .fq-row, .fq-reason"), x => x.TextContent.Contains("Arrears", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("not recorded as follow-up history", cut.Markup);
        }, RenderTimeout);
    }

    [Fact]
    public void SpaceOperationScopes_AreServerDriven_AndNeverInventAFacility()
    {
        var kanmanggay = new FollowUpScopeDto(FollowUpScopeKind.MonthlySpace, "KANMANGGAY_SPACE_RENTAL", "Kanmanggay");
        var fiesta = new FollowUpScopeDto(FollowUpScopeKind.EventLot, "FIESTA_ARAW_LOT_RENTAL", "Fiesta / Araw");
        var queue = SampleQueue() with
        {
            ObligationItems = new[]
            {
                new ObligationFollowUpItemDto(Guid.NewGuid(), Guid.NewGuid(), "Lisa Ilogans", "K-3", kanmanggay,
                    new DateOnly(2026, 8, 1), 1_000m, 400m, 600m, "High", null, "/kanmanggay"),
                new ObligationFollowUpItemDto(Guid.NewGuid(), Guid.NewGuid(), "Ben Cruz", "Lot 1", fiesta,
                    new DateOnly(2026, 8, 15), 500m, 0m, 500m, "Normal", LotRentalEvent.Fiesta, "/fiesta")
            }
        };
        var (cut, _) = RenderQueue(queue);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("All sources", cut.Find(".fq-source-select .fh-dd-trigger").TextContent);
            Assert.Empty(cut.FindAll("[aria-label='Filter by source'] .fq-chip"));
            Assert.Single(cut.FindAll(".fq-source-select .fh-dd-trigger"));
            Assert.Contains("Unpaid and partial periods", cut.Markup);
            Assert.Contains("August 2026", cut.Markup);                       // monthly period
            Assert.Contains("Fiesta · Aug 15, 2026", cut.Markup);             // event period
            Assert.Contains("Partial · ₱400 paid", cut.Markup);
            Assert.DoesNotContain("KANMANGGAY_SPACE_RENTAL", cut.Markup);       // no raw code
            Assert.Null(cut.FindAll(".fq-priority").FirstOrDefault(p => p.TextContent.Contains("Critical")));
        }, RenderTimeout);

        cut.Find(".fq-source-select .fh-dd-trigger").Click();
        cut.FindAll(".fq-source-select [role='option']").Single(c => c.TextContent.Contains("Kanmanggay")).Click();
        Assert.Contains("Lisa Ilogans", cut.Markup);
        Assert.DoesNotContain("Ben Cruz", cut.Markup);
        Assert.DoesNotContain("Lower-age delinquent", cut.Markup);            // classic rows are not mixed into a space scope

        cut.Find(".fq-source-select .fh-dd-trigger").Click();
        cut.FindAll(".fq-source-select [role='option']").Single(c => c.TextContent.Trim() == "TCC" || c.TextContent.Contains("Tampak Commercial Center")).Click();
        Assert.DoesNotContain("Lisa Ilogans", cut.Markup);
    }

    [Fact]
    public void FilterToolbarKeepsReasonShortcutsAndSourcePickerWithoutRemovingFollowUpRecords()
    {
        var (cut, _) = RenderQueue();
        cut.WaitForAssertion(() => Assert.NotNull(RowFor(cut, "Lower-age delinquent")), RenderTimeout);
        Assert.Single(cut.FindAll(".fq-filterbar"));
        Assert.Single(cut.FindAll(".fq-filterbar > .fq-reasons"));
        Assert.Single(cut.FindAll(".fq-navrow"));
        Assert.Single(cut.FindAll(".fq-navrow nav[aria-label='Monitoring registers']"));
        Assert.Single(cut.FindAll(".fq-navrow .fq-search"));
        Assert.Single(cut.FindAll(".fq-navrow .fq-history-link"));
        Assert.Single(cut.FindAll(".fq-filterbar > .fq-source-select"));
        Assert.Empty(cut.FindAll(".fq-filterbar .fq-search, .fq-filterbar .fq-history-link"));
        Assert.Single(cut.FindAll(".fq-source-select .fh-dd-trigger"));
        Assert.Collection(cut.FindAll(".fq-reasons .fq-chip"),
            x => Assert.StartsWith("All", x.TextContent.Trim()),
            x => Assert.StartsWith("Delinquent", x.TextContent.Trim()),
            x => Assert.StartsWith("Arrears", x.TextContent.Trim()),
            x => Assert.StartsWith("Current-period", x.TextContent.Trim()),
            x => Assert.StartsWith("Contract", x.TextContent.Trim()),
            x => Assert.StartsWith("Excused Review", x.TextContent.Trim()),
            x => Assert.StartsWith("Miscellaneous", x.TextContent.Trim()));
        Assert.DoesNotContain(cut.FindAll(".fq-reasons .fq-chip"),
            x => x.TextContent.Contains("Missing OR") || x.TextContent.Contains("NPM Daily"));
        Assert.NotNull(RowFor(cut, "Lower-age delinquent"));
        Assert.Contains("Past-period view", cut.Find(".fq-history-link").TextContent);
    }

    [Fact]
    public void WholeTimeHistoryHidesTwoReasonShortcutsButKeepsTheirSourceRowsInAll()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var sample = SampleQueue();
        var npmDaily = new FollowUpItemDto(
            4, "Normal", "NPM missed collection", "npm", FacilityCode.NPM, "Daily stall",
            "NPM daily coverage", "1 missed day", null, false, "September 2026",
            "Daily coverage gap", "Open daily calendar", "/npm");
        var history = sample with { Items = sample.Items.Append(npmDaily).ToArray() };
        var reports = new Mock<IReportsApiClient>();
        reports.Setup(a => a.GetFollowUpHistoryYearsAsync())
            .ReturnsAsync(Result<IReadOnlyList<int>>.Success([2026]));
        reports.Setup(a => a.GetFollowUpHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<FollowUpQueueDto>.Success(history));
        Services.AddSingleton(reports.Object);

        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(a => a.GetClosedStallAccountsAsync())
            .ReturnsAsync(Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Stalls.ClosedStallAccountDto>>.Success([]));
        Services.AddSingleton(stalls.Object);

        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("Unavailable", 500));
        Services.AddSingleton(facilities.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));

        var branding = new Mock<IMunicipalitiesApiClient>();
        branding.Setup(a => a.GetCurrentBrandingAsync())
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(branding.Object));
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IObligationsApiClient>());
        Services.AddSingleton(Mock.Of<IPenaltiesApiClient>());
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
        Services.AddSingleton(Mock.Of<ITrmApiClient>());
        Services.AddSingleton(Mock.Of<ITpmApiClient>());
        Services.AddSingleton(Mock.Of<ISlaughterApiClient>());
        this.AddTestAuthorization().SetAuthorized("SuperAdmin");

        var cut = RenderComponent<EEMOCantilanSDS.Client.Components.Pages.Reports.PastFollowUpQueue>();
        cut.WaitForAssertion(() =>
        {
            var chips = cut.FindAll(".fh-reasonrow .fh-chip").Select(x => x.TextContent).ToArray();
            Assert.Equal(8, chips.Length);
            Assert.DoesNotContain(chips, x => x.Contains("Missing OR") || x.Contains("NPM Daily"));
            Assert.Contains(chips, x => x.Contains("Closed"));
            Assert.Contains(cut.FindAll(".fh-row"), x => x.TextContent.Contains("Receipt follow-up"));
            Assert.DoesNotContain(cut.FindAll(".fh-section"), s =>
                s.TextContent.Contains("Needs immediate action") && s.TextContent.Contains("Receipt follow-up"));
            Assert.Contains(cut.FindAll(".fh-section"), s =>
                s.TextContent.Contains("Facility-specific operational") && s.TextContent.Contains("Receipt follow-up"));
            Assert.Contains(cut.FindAll(".fh-row"), x => x.TextContent.Contains("NPM daily coverage"));
            Assert.Contains("9", chips[0]); // All includes all nine source rows.
        }, RenderTimeout);
        reports.Verify(a => a.GetFollowUpHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), true), Times.Once);
    }

    [Fact]
    public void WholeTimeHistoryListsSpaceAccounts_OneRowPerAccount_CurrentAndEarlierTermsApart_AndSearchFindsThem()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var greggor = SpaceMonth("Greggor Dal", new(2025, 11, 1), 10_000m, "arrears", "Expired contract", new(2025, 10, 24));
        var lorenofs = Guid.NewGuid();
        var history = SampleQueue() with
        {
            ObligationItems =
            [
                greggor, greggor with { PeriodStart = new(2025, 12, 1) },
                // A renewed space: an earlier-term month and a current-term month, stated apart.
                SpaceMonth("Lorenofs Brando", new(2025, 9, 1), 700m, "arrears", "Expired contract", new(2028, 10, 24), earlier: true) with { AccountId = lorenofs },
                SpaceMonth("Lorenofs Brando", new(2025, 12, 1), 300m, "arrears", "Previous year", new(2028, 10, 24)) with { AccountId = lorenofs }
            ]
        };
        var reports = new Mock<IReportsApiClient>();
        reports.Setup(a => a.GetFollowUpHistoryYearsAsync()).ReturnsAsync(Result<IReadOnlyList<int>>.Success([2026]));
        reports.Setup(a => a.GetFollowUpHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<FollowUpQueueDto>.Success(history));
        Services.AddSingleton(reports.Object);
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(a => a.GetClosedStallAccountsAsync()).ReturnsAsync(Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Stalls.ClosedStallAccountDto>>.Success([]));
        Services.AddSingleton(stalls.Object);
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("Unavailable", 500));
        Services.AddSingleton(facilities.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));
        var branding = new Mock<IMunicipalitiesApiClient>();
        branding.Setup(a => a.GetCurrentBrandingAsync()).ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(branding.Object));
        foreach (var mock in new object[] { Mock.Of<ISetupApiClient>(), Mock.Of<IPaymentsApiClient>(), Mock.Of<IObligationsApiClient>(), Mock.Of<IPenaltiesApiClient>(),
                     Mock.Of<IDailyCollectionApiClient>(), Mock.Of<ITrmApiClient>(), Mock.Of<ITpmApiClient>(), Mock.Of<ISlaughterApiClient>() })
            Services.AddSingleton(mock.GetType().GetInterfaces().First(i => i.Name.EndsWith("ApiClient")), mock);
        this.AddTestAuthorization().SetAuthorized("SuperAdmin");

        var cut = RenderComponent<EEMOCantilanSDS.Client.Components.Pages.Reports.PastFollowUpQueue>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#fh-space-title")), RenderTimeout);
        var rows = cut.Find("#fh-space-title").Closest("section")!.QuerySelectorAll("tbody tr");
        Assert.Equal(2, rows.Length);                                                               // per account, not per month
        var greggorRow = rows.Single(r => r.TextContent.Contains("Greggor Dal"));
        Assert.Contains("₱20,000", greggorRow.TextContent);
        Assert.Contains("term ended Oct 24, 2025", greggorRow.TextContent);
        var lorenofsRow = rows.Single(r => r.TextContent.Contains("Lorenofs Brando"));
        Assert.Contains("₱300 · 1 mo", lorenofsRow.TextContent);                                    // current term
        Assert.Contains("₱700 · 1 mo", lorenofsRow.TextContent);                                    // earlier term, apart
        Assert.Contains("₱1,000", lorenofsRow.TextContent);                                         // lifetime, each period once

        cut.Find(".fh-search input[type=search], input[aria-label='Search history']").Input("Greggor");
        var found = cut.Find("#fh-space-title").Closest("section")!.QuerySelectorAll("tbody tr");
        Assert.Single(found);
        Assert.Contains("Greggor Dal", found[0].TextContent);
    }

    [Fact]
    public void ReviewCarriesActualAccountAndPeriodToExistingPaymentDialogWithoutNestedReview()
    {
        var queue=SampleQueue(); var account=queue.Items.First();
        var payments=new Mock<IPaymentsApiClient>();
        payments.Setup(x=>x.GetOutstandingMonthsAsync(account.StallId!.Value,account.ContractId,null,null))
            .ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([new("2026-08",PaymentStatus.Partial,1000,200,800,null,null)]));
        payments.Setup(x=>x.GetOutstandingMonthsAsync(account.StallId!.Value,account.ContractId,2026,8))
            .ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([new("2026-08",PaymentStatus.Partial,1000,200,800,null,null)]));
        var (cut,_)=RenderQueue(queue,payments.Object);
        cut.WaitForAssertion(()=>Assert.NotNull(RowFor(cut,account.Person)),RenderTimeout);
        RowFor(cut,account.Person).QuerySelector("button")!.Click();
        cut.WaitForAssertion(()=>Assert.Single(cut.FindComponents<FollowUpAccountReview>()),RenderTimeout);
        var review=cut.FindComponent<FollowUpAccountReview>();
        Assert.Single(review.FindAll(".review-footer-actions"));
        Assert.Contains("Queue follow-up amount", review.Find(".review-queue-position").TextContent);
        Assert.Contains("₱4,800.00", review.Find(".review-queue-position").TextContent);
        Assert.Contains("12 months to August 2026", review.Find(".review-queue-position").TextContent);
        Assert.Contains("Total outstanding balance · all periods", review.Find(".review-summary").TextContent);
        Assert.Contains("₱800.00", review.Find(".review-summary").TextContent);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("account-review-payment",review.Markup);
        review.Find(".review-period-toggle").Click();
        review.Find(".btn-primary").Click();
        Assert.Empty(cut.FindComponents<FollowUpAccountReview>());
        var form=cut.FindComponent<PayBillModal>().Instance;
        Assert.True(form.Show); Assert.Equal(account.StallId,form.StallId);
        Assert.Equal(account.ContractId,form.ContractId); Assert.Equal(2026,form.PeriodYear); Assert.Equal(8,form.PeriodMonth);
    }

    [Theory]
    [InlineData("Current unpaid")]
    [InlineData("Current partial")]
    public void Current_period_account_review_opens_existing_record_payment_modal(string occupant)
    {
        var queue = SampleQueue();
        var account = queue.Items.Single(x => x.Person == occupant);
        var payments = new Mock<IPaymentsApiClient>();
        payments.Setup(x => x.GetOutstandingMonthsAsync(account.StallId!.Value, account.ContractId, null, null))
            .ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([
                new("2026-09", PaymentStatus.Unpaid, 1200, 0, 1200, null, null)]));
        var (cut, _) = RenderQueue(queue, payments.Object);
        cut.WaitForAssertion(() => Assert.NotNull(RowFor(cut, occupant)), RenderTimeout);
        RowFor(cut, occupant).QuerySelector("button")!.Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindComponents<FollowUpAccountReview>()), RenderTimeout);
        var review = cut.FindComponent<FollowUpAccountReview>();
        review.WaitForAssertion(() => Assert.Contains("Record payment", review.Find(".review-footer-actions").TextContent), RenderTimeout);
        review.FindAll(".review-footer-actions button").Single(b => b.TextContent.Contains("Record payment")).Click();
        var payment = cut.FindComponent<PayBillModal>().Instance;
        Assert.True(payment.Show);
        Assert.Equal(account.StallId, payment.StallId);
        Assert.Equal(account.Facility, payment.Facility);
    }

    [Fact]
    public void AccountReviewLabelsCurrentAndPastYearsWithoutChangingPeriodBalances()
    {
        var queue = SampleQueue();
        var account = queue.Items.First();
        var currentYear = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year;
        var payments = new Mock<IPaymentsApiClient>();
        payments.Setup(x => x.GetOutstandingMonthsAsync(account.StallId!.Value, account.ContractId, null, null))
            .ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([
                new($"{currentYear - 1}-08", PaymentStatus.Unpaid, 900, 0, 900, null, null),
                new($"{currentYear}-08", PaymentStatus.Partial, 1000, 200, 800, null, null)
            ]));
        var (cut, _) = RenderQueue(queue, payments.Object);
        cut.WaitForAssertion(() => Assert.NotNull(RowFor(cut, account.Person)), RenderTimeout);
        RowFor(cut, account.Person).QuerySelector("button")!.Click();
        cut.WaitForAssertion(() =>
        {
            var review = cut.FindComponent<FollowUpAccountReview>();
            Assert.Equal(2, review.FindAll(".review-year-badge").Count);
            Assert.Contains("Past year", review.FindAll(".review-year-badge")[0].TextContent);
            Assert.Contains("Current year", review.FindAll(".review-year-badge")[1].TextContent);
            Assert.Contains("₱1,700.00", review.Find(".review-summary").TextContent);
            Assert.Contains("₱4,800.00", review.Find(".review-queue-position").TextContent);
        }, RenderTimeout);
    }

    private (IRenderedComponent<FollowUpPage> Cut, Mock<IReportsApiClient> Api) RenderQueue(FollowUpQueueDto? queue = null, IPaymentsApiClient? payments = null)
    {
        JSInterop.Mode=JSRuntimeMode.Loose;
        var reportsApi = new Mock<IReportsApiClient>();
        reportsApi.Setup(api => api.GetFollowUpQueueAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<FollowUpQueueDto>.Success(queue ?? SampleQueue()));
        Services.AddSingleton(reportsApi.Object);

        var facilitiesApi = new Mock<IFacilitiesApiClient>();
        facilitiesApi.Setup(api => api.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("Unavailable", 500));
        Services.AddSingleton<IFacilitiesApiClient>(facilitiesApi.Object);
        Services.AddSingleton(new FacilityState(facilitiesApi.Object));

        var municipalitiesApi = new Mock<IMunicipalitiesApiClient>();
        municipalitiesApi.Setup(api => api.GetCurrentBrandingAsync())
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(municipalitiesApi.Object));

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(payments ?? Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IObligationsApiClient>());
        Services.AddSingleton(Mock.Of<IPenaltiesApiClient>());
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
        Services.AddSingleton(Mock.Of<ITrmApiClient>());
        Services.AddSingleton(Mock.Of<ITpmApiClient>());
        Services.AddSingleton(Mock.Of<ISlaughterApiClient>());
        this.AddTestAuthorization().SetAuthorized("SuperAdmin");

        return (RenderComponent<FollowUpPage>(), reportsApi);
    }

    private static FollowUpQueueDto SampleQueue() => new(
        "September 2026",
        new DateOnly(2026, 9, 24),
        new FollowUpItemDto[]
        {
            Item(2, "Normal", "Delinquent", "delinquent", "Lower-age delinquent", "TCC · Stall 04", 4_800m,
                "12 months to August 2026", "Unpaid · 2 months", "View vendor", "/profile/tcc/00000000-0000-0000-0000-000000000001"),
            Item(1, "Critical", "Delinquent", "delinquent", "Higher-age delinquent", "TCC · Stall 08", 9_600m,
                "12 months to August 2026", "Unpaid · 4 months", "View vendor", "/profile/tcc/00000000-0000-0000-0000-000000000002"),
            Item(2, "Normal", "Current-period unpaid", "current", "Current unpaid", "TCC · Stall 10", 1_200m,
                "September 2026", "Unpaid", "View vendor", "/profile/tcc/00000000-0000-0000-0000-000000000003"),
            Item(2, "Normal", "Partial payment", "current", "Current partial", "NCC · Stall 11", 600m,
                "September 2026", "Partial", "View vendor", "/profile/ncc/00000000-0000-0000-0000-000000000004"),
            Item(1, "High", "Past occupancy balance", "delinquent", "Lapsed-term occupant", "TCC · Stall 12", 3_200m,
                "Jan–Aug 2026", "No longer the occupant", "Review account", "/profile/tcc/00000000-0000-0000-0000-000000000005"),
            Item(2, "Normal", "Contract expiring", "contract", "Expiring account", "BBQ · Stall 2", null,
                "December 31, 2026", "Expiring soon", "Review contract", "/profile/bbq/00000000-0000-0000-0000-000000000006"),
            Item(4, "Normal", "OR not recorded", "missingor", "Receipt follow-up", "TCC · Stall 13", 800m,
                "September 2026", "Paid · OR blank", "Add OR", "/profile/tcc/00000000-0000-0000-0000-000000000007"),
            Item(2, "Normal", "Electricity unpaid", "misc", "Utility follow-up", "NPM · Stall 14", 400m,
                "September 2026", "Unpaid", "Pay Bill", "/npm")
        });

    private static FollowUpItemDto Item(
        int section,
        string priority,
        string reason,
        string reasonKind,
        string person,
        string identifier,
        decimal? amount,
        string period,
        string status,
        string action,
        string link) => new(
            section, priority, reason, reasonKind, FacilityCode.TCC, "Monthly rental", person, identifier,
            amount, false, period, status, action, link,
            StallId: Guid.NewGuid());

    private static IElement RowFor(IRenderedComponent<FollowUpPage> cut, string person) =>
        cut.FindAll(".fq-row").Single(row => row.TextContent.Contains(person, StringComparison.Ordinal));
}
