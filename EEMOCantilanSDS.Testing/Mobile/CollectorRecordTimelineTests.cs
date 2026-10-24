using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Records;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public sealed class CollectorRecordTimelineTests
{
    [Fact]
    public void Actual_recording_time_interleaves_all_facilities_and_canonical_SRCs()
    {
        var day = new DateOnly(2026, 10, 24);
        var recent = new MobileCollectorRecordDto("OLD", "Recent monthly", FacilityCode.NCC, "NCC", "SP-1", "Rent",
            2400m, 2400m, false, day.ToDateTime(new TimeOnly(11, 00)));
        var early = new MobileCollectorRecordDto("OLDER", "Early market", FacilityCode.NPM, "NPM", "F1", "Market",
            30m, 30m, false, day.ToDateTime(new TimeOnly(9, 00)));
        var canonical = new OperationRecord(Guid.NewGuid(), day, "SRC-2026-000009", RevenueInstrumentType.OfficialReceipt,
            "Canonical operation", "Posted", 150m, [new("Comfort room", "Direct", 150m)],
            RecordedAtUtc: new DateTime(2026, 10, 24, 2, 0, 0, DateTimeKind.Utc)); // 10 AM in PH

        var feed = CollectorRecordTimeline.Build([early, recent], [canonical]);

        Assert.Equal(3, feed.Count);
        Assert.Equal("Recent monthly", feed[0].Legacy?.Primary.PayorName);
        Assert.Equal("SRC-2026-000009", feed[1].Canonical?.ReferenceCode);
        Assert.Equal("Early market", feed[2].Legacy?.Primary.PayorName);
        Assert.Equal(2580m, feed.Sum(x => x.Canonical?.Total ?? x.Legacy?.TotalPaid ?? 0m));
    }

    [Fact]
    public void Multiple_legacy_receipts_for_one_payor_on_one_day_remain_one_entry_with_all_receipts()
    {
        var day = new DateOnly(2026, 10, 24);
        var before = new MobileCollectorRecordDto("OR-A", "Same payor", FacilityCode.NPM, "NPM", "F2", "Market",
            30m, 30m, false, day.ToDateTime(new TimeOnly(9, 0)));
        var after = new MobileCollectorRecordDto("OR-B", "Same payor", FacilityCode.NPM, "NPM", "F2", "Market",
            30m, 30m, false, day.ToDateTime(new TimeOnly(11, 0)));

        var feed = CollectorRecordTimeline.Build([before, after], []);

        Assert.Single(feed);
        Assert.Equal(2, feed[0].Legacy!.ReceiptCount);
        Assert.Equal(60m, feed[0].Legacy!.TotalPaid);
        Assert.Equal(11, feed[0].RecordedAt.Hour);
    }

    [Fact]
    public void Older_server_payload_without_recorded_timestamp_falls_back_to_business_date()
    {
        var day = new DateOnly(2026, 10, 24);
        var oldFormat = new OperationRecord(Guid.NewGuid(), day, "SRC-2026-000001", null,
            "Legacy API format", "Posted", 50m, []);
        var feed = CollectorRecordTimeline.Build([], [oldFormat]);
        Assert.Single(feed);
        Assert.Equal(day.ToDateTime(TimeOnly.MinValue), feed[0].RecordedAt);
    }
}
