using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed partial class FineAssessmentComponentTests
{
    private FineAssessmentDto Late(string code, string name, int month, DateOnly charged, decimal amount) =>
        new(Guid.NewGuid(), _account, "Liable holder", "Space 4", "Kanmanggay", name, $"{name} on rent", charged, charged, null,
            amount, 0m, amount, "Outstanding", 1, "RA 7160 Sec. 168", DateTime.UtcNow, null,
            new(new(FineAffectedPeriodKind.ObligationPeriod, new DateOnly(2026, month, 1), new DateOnly(2026, month, 1).AddMonths(1).AddDays(-1)), new DateOnly(2026, month, 1).ToString("MMMM yyyy")),
            true, code);

    [Fact]
    public void LateCharges_AreOneLine_ThatOpensToEachMonthsSurchargeAndInterest_AndOtherFinesStayAsTheyAre()
    {
        var sepInterest2 = Late(FineAssessment.InterestCode, "Interest", 9, new(2026, 10, 21), 25m);
        var rows = new[]
        {
            Late(FineAssessment.InterestCode, "Interest", 10, new(2026, 10, 21), 25m), sepInterest2,
            Late(FineAssessment.SurchargeCode, "Surcharge", 9, new(2026, 9, 21), 250m),
            Late(FineAssessment.InterestCode, "Interest", 9, new(2026, 9, 21), 25m),
            Late(FineAssessment.SurchargeCode, "Surcharge", 10, new(2026, 10, 21), 250m),
            Row(),
        };
        _api.Setup(x => x.GetAssessmentsAsync(_account.Kind, _account.AccountId, null)).ReturnsAsync(Result<IReadOnlyList<FineAssessmentDto>>.Success(rows));
        _api.Setup(x => x.QuoteFineAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => Result<FineSettlementQuoteDto>.Success(
            new(id, _account, "Liable holder", "Kanmanggay", "Space 4", 25, 0, 25, "Outstanding", 1, "quote", RevenueInstrumentType.OfficialReceipt, true, null)));
        Guid? paid = null;
        var cut = RenderComponent<AccountFineAssessments>(p => p.Add(x => x.Account, _account).Add(x => x.OnPay, (Guid id) => paid = id));

        cut.WaitForAssertion(() => Assert.Contains("Late charges", cut.Find(".late-summary").TextContent));
        Assert.Contains("2 months", cut.Find(".late-summary").TextContent);
        Assert.Contains("₱575.00", cut.Find(".late-summary").TextContent);
        Assert.Empty(cut.FindAll(".late-table"));
        Assert.Single(cut.FindAll(".fine-row"));   // the other fine keeps its own card

        cut.Find(".late-summary").Click();

        var months = cut.FindAll(".late-month").Select(m => m.TextContent).ToList();
        Assert.Equal(2, months.Count);
        Assert.Contains("September 2026", months[0]);
        Assert.Contains("₱300.00", months[0]);
        var september = cut.FindAll(".late-table tbody")[0].QuerySelectorAll("tr:not(.late-month)").Select(r => r.QuerySelector("td")!.TextContent).ToList();
        Assert.Equal(new[] { "Surcharge", "Interest", "Interest" }, september);

        cut.FindAll(".late-table tbody")[0].QuerySelectorAll("tr:not(.late-month)")[2].QuerySelector("button")!.Click();
        Assert.Equal(sepInterest2.Id, paid);
    }
}
