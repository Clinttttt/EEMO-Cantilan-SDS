using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.ComponentTests.Services;

/// <summary>The Review drawer kept the amount it opened with after a payment reloaded the queue behind it.</summary>
public sealed class FollowUpReviewContextTests
{
    private static readonly Guid Stall = Guid.NewGuid();

    private static FollowUpItemDto Row(decimal amount, string period, string kind = "delinquent", Guid? stall = null) =>
        new(1, "High", "Delinquent", kind, FacilityCode.TCC, "Monthly rental", "Occupant", "4", amount, false, period, "Unpaid", "View", "/profile/tcc/4", stall ?? Stall);

    private static readonly FollowUpReviewContext Before = new(Row(7200m, "August 2026"), null, new DateOnly(2026, 11, 17));

    [Fact]
    public void After_paying_the_oldest_month_it_shows_the_same_rows_new_amount()
    {
        var now = Before.Reloaded([Row(4800m, "September 2026", stall: Guid.NewGuid()), Row(4800m, "September 2026")], _ => true);

        Assert.Equal(4800m, now!.FacilityItem!.Amount);
        Assert.Equal(Stall, now.FacilityItem.StallId);
        Assert.True(now.CanRecordPayment);
    }

    [Fact]
    public void A_row_that_left_the_queue_leaves_nothing_to_review()
    {
        Assert.Null(Before.Reloaded([Row(4800m, "September 2026", kind: "missingor")], _ => true));
    }

    [Fact]
    public void Without_a_reloaded_queue_it_is_unchanged()
    {
        Assert.Same(Before, Before.Reloaded(null, _ => true));
    }
}
