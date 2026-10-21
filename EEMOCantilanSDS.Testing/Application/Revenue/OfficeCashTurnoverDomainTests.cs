using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

public sealed class OfficeCashTurnoverDomainTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 18);

    private static OfficeCashTurnover Record(decimal expected = 600m, decimal turned = 600m, string? reference = "DEP-1", string by = "Cashier",
        string? remarks = null, DateOnly? date = null, DateOnly? from = null, int count = 1, Guid? operation = null, DateTime? at = null) =>
        OfficeCashTurnover.Record(Tenant, date ?? Day, from ?? Day, Day, expected, turned, count, reference, by, remarks,
            operation ?? Guid.NewGuid(), "fingerprint", "head", "actor-1", at ?? DateTime.UtcNow);

    [Fact]
    public void An_exact_turnover_records_the_evidence_and_has_no_difference()
    {
        var t = Record();
        Assert.Equal((OfficeCashTurnoverStatus.Recorded, 0m, false, "DEP-1", "Cashier"), (t.Status, t.DifferenceAmount, t.NeedsReview, t.Reference, t.TurnedOverBy));
    }

    [Theory]
    [InlineData(500, 100, true)]    // shortfall is positive
    [InlineData(650, -50, true)]    // excess is negative
    public void A_difference_is_kept_visible_and_flagged_for_review(double turned, double difference, bool review)
    {
        var t = Record(turned: (decimal)turned, remarks: "Counted again");
        Assert.Equal(((decimal)difference, review), (t.DifferenceAmount, t.NeedsReview));
    }

    [Fact]
    public void A_difference_without_an_explanation_is_refused() =>
        Assert.Throws<ArgumentException>(() => Record(turned: 500m));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Evidence_and_the_handing_person_are_never_invented(string? blank)
    {
        Assert.Null(Record(reference: blank).Reference);                                         // the supporting document reference is optional, never invented
        Assert.Throws<ArgumentException>(() => Record(by: blank!));
    }

    [Fact]
    public void Amounts_dates_counts_identity_and_time_are_validated()
    {
        Assert.Throws<ArgumentException>(() => Record(turned: 0m));
        Assert.Throws<ArgumentException>(() => Record(turned: 600.005m));
        Assert.Throws<ArgumentException>(() => Record(expected: 0m, turned: 1m));
        Assert.Throws<ArgumentException>(() => Record(count: 0));
        Assert.Throws<ArgumentException>(() => Record(date: Day.AddDays(-1)));                 // before the cash was collected
        Assert.Throws<ArgumentException>(() => Record(from: Day.AddDays(1)));                  // period reversed
        Assert.Throws<ArgumentException>(() => Record(operation: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Record(at: DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)));
        Assert.Throws<ArgumentException>(() => Record(reference: new string('x', 101)));
    }

    [Fact]
    public void Voiding_keeps_the_record_and_needs_a_reason_the_actor_and_a_single_use()
    {
        var t = Record();
        Assert.Throws<ArgumentException>(() => t.Void("  ", "head", "actor-2", DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => t.Void("Wrong slip", "", "actor-2", DateTime.UtcNow));
        t.Void("Wrong slip", "head", "actor-2", DateTime.UtcNow);
        Assert.Equal((OfficeCashTurnoverStatus.Voided, "Wrong slip", "actor-2", false), (t.Status, t.VoidReason, t.VoidedByActorId, t.NeedsReview));
        Assert.Equal("DEP-1", t.Reference);                                                      // history survives the void
        Assert.Throws<InvalidOperationException>(() => t.Void("Again", "head", "actor-2", DateTime.UtcNow));
    }

    [Fact]
    public void Coverage_keeps_the_original_actor_and_is_released_not_deleted()
    {
        var c = OfficeCashTurnoverCoverage.Cover(Tenant, Guid.NewGuid(), Guid.NewGuid(), 600m, "orig-actor", "Original Clerk");
        Assert.Equal((true, "orig-actor", "Original Clerk"), (c.IsActive, c.OriginalActorId, c.OriginalActorName));
        c.Release();
        Assert.False(c.IsActive);
        Assert.Throws<ArgumentException>(() => OfficeCashTurnoverCoverage.Cover(Tenant, Guid.NewGuid(), Guid.NewGuid(), 0m, "a", "b"));
        Assert.Throws<ArgumentException>(() => OfficeCashTurnoverCoverage.Cover(Tenant, Guid.NewGuid(), Guid.NewGuid(), 1m, "", "b"));
    }

    [Fact]
    public void The_module_is_off_by_default() => Assert.False(new OfficeCashTurnoverOptions().Enabled);
}
