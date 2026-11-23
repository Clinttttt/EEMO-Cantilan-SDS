using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed partial class FollowUpAccountReviewTests
{
    private static StallStatementDto LateStatement(Guid? contract) => new(
        new(2026, 11, 24), 0, 0m, 2, 2_000m, 1_000m, 0m, 1_000m,
        [new UnpaidMonthDto(2026, 9, 1_000m), new UnpaidMonthDto(2026, 10, 1_000m)], [],
        PenaltiesDue: 575m, CurrentDueDate: new(2026, 11, 20),
        LateCharges: [new LateChargeMonthDto(2026, 9, 250m, 50m), new LateChargeMonthDto(2026, 10, 250m, 25m)],
        SurchargePercent: 25m, InterestPercent: 2m, ContractId: contract);

    private IRenderedComponent<FollowUpAccountReview> ReviewLateSpace(Guid? rowContract, Guid statementContract)
    {
        var stall = Guid.NewGuid();
        var item = new FollowUpItemDto(1, "High", "Delinquent", "delinquent", FacilityCode.TCC, "Monthly rental", "Maria Santos", "7", 2_000m, false,
            "September 2026", "Unpaid", "View", "/profile/tcc/7", stall, ContractId: rowContract);
        _payments.Setup(x => x.GetOutstandingMonthsAsync(stall, rowContract, null, null)).ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success(
            [new("2026-09", PaymentStatus.Unpaid, 1_000m, 0m, 1_000m, null, null), new("2026-10", PaymentStatus.Unpaid, 1_000m, 0m, 1_000m, null, null)]));
        _payments.Setup(x => x.GetStallStatementAsync(stall)).ReturnsAsync(Result<StallStatementDto>.Success(LateStatement(statementContract)));
        return RenderComponent<FollowUpAccountReview>(p => p.Add(x => x.Context, new(item, null, new(2026, 11, 24))));
    }

    [Fact]
    public void AReviewedSpace_ShowsItsAccountSummary_WithSurchargeAndInterestApartFromTheRent()
    {
        var contract = Guid.NewGuid();
        var cut = ReviewLateSpace(contract, contract);

        cut.WaitForAssertion(() => Assert.Single(cut.FindComponents<AccountSummary>()));
        var rows = cut.FindAll(".acct-row").Select(r => r.TextContent).ToList();
        Assert.Contains(rows, r => r.Contains("Surcharges (25%)") && r.Contains("₱500.00"));
        Assert.Contains(rows, r => r.Contains("Accrued interest (2% a month)") && r.Contains("₱75.00"));
        Assert.Contains(rows, r => r.Contains("Total amount due") && r.Contains("₱3,575.00"));
    }

    [Fact]
    public void AnEarlierTermsRow_NeverShowsThePresentAccountsSummaryAsItsOwn()
    {
        var cut = ReviewLateSpace(Guid.NewGuid(), Guid.NewGuid());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("section[aria-label='Outstanding billing periods']")));
        Assert.Empty(cut.FindComponents<AccountSummary>());
    }
}
