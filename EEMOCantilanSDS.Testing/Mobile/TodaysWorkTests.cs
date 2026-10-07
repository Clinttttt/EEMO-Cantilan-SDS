using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Presentation;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class TodaysWorkTests
{
    [Fact]
    public void Terminal_catalog_group_does_not_fall_into_other_operations()
    {
        var item = new WorkItem("Comfort Room", "Ready", WorkTarget.Operation, true,
            OperationCode: "TERMINAL_COMFORT_ROOM");
        Assert.Equal(WorkSections.Terminal, Assert.Single(WorkSections.Group([item])).Name);
    }

    [Fact]
    public void Ready_terminal_is_first_class_and_transportation_stays_separate()
    {
        var work = Build([Op(CollectorOperationCodes.Terminal, CollectorOperationCapabilityStatus.Ready),
            Op(CollectorOperationCodes.Transportation, CollectorOperationCapabilityStatus.Ready)]);
        var sections = WorkSections.Group(work.Available);
        var terminal = Assert.Single(sections.Single(x => x.Name == WorkSections.Terminal).Items);
        Assert.Equal(CollectorOperationCodes.Terminal, terminal.OperationCode);
        Assert.True(terminal.CanOpen);
        Assert.Equal(WorkTarget.Operation, terminal.Target);
        Assert.Equal("Ready", terminal.Status);
        Assert.Equal(CollectorOperationCodes.Transportation,
            Assert.Single(sections.Single(x => x.Name == WorkSections.Market).Items).OperationCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Legacy_terminal_facility_is_not_current_work_even_without_ready_terminal(bool available)
    {
        var work = Build(facilities: [new(FacilityCode.TRM, "Transport Terminal", "", true, available, BillingArchetype.PerTrip)]);
        Assert.Empty(work.Available);
        Assert.Empty(work.AssignedUnavailable);
    }

    [Fact]
    public void Presentation_sections_preserve_every_authorized_row_and_its_capability()
    {
        WorkItem[] rows = [new("Public Market", "Ready", WorkTarget.Facility, true, "NPM"),
            new("Iceplant", "Ready", WorkTarget.Facility, true, "ICE"),
            new("Water", "Ready", WorkTarget.Operation, true, OperationCode: CollectorOperationCodes.Wcf),
            new("Future service", "Ready", WorkTarget.None, false, OperationCode: "FUTURE")];
        var grouped = WorkSections.Group(rows);
        Assert.Equal(rows.Length, grouped.Sum(g => g.Items.Count));
        Assert.All(rows, row => Assert.Same(row, Assert.Single(grouped.SelectMany(g => g.Items), item => item == row)));
        Assert.Equal(WorkSections.Market, grouped[0].Name);
        Assert.False(Assert.Single(grouped.Single(g => g.Name == "Other operations").Items).CanOpen);
    }

    [Fact]
    public void Rows_are_grouped_under_the_official_monthly_income_families()
    {
        WorkItem[] rows = [new("Public Market", "Ready", WorkTarget.Facility, true, "NPM"),
            new("Kanmanggay", "Ready", WorkTarget.Operation, true, OperationCode: CollectorOperationCodes.KanmanggaySpaceRental),
            new("Fiesta / Araw", "Ready", WorkTarget.Operation, true, OperationCode: CollectorOperationCodes.FiestaArawLotRental),
            new("Transportation / Parking", "Ready", WorkTarget.Operation, true, OperationCode: CollectorOperationCodes.Transportation),
            new("Slaughterhouse", "Ready", WorkTarget.Facility, true, "SLH")];

        var sections = WorkSections.Group(rows).ToDictionary(g => g.Name, g => g.Items.Select(i => i.Name).ToArray());

        Assert.Equal(["Public Market"], sections[WorkSections.Rent]);
        Assert.Equal(["Kanmanggay", "Fiesta / Araw"], sections[WorkSections.Space]);
        Assert.Equal(["Transportation / Parking"], sections[WorkSections.Market]);       // separate from Terminal, even though both use CT
        Assert.Equal(["Slaughterhouse"], sections[WorkSections.Slaughterhouse]);
        Assert.DoesNotContain(WorkSections.Terminal, sections.Keys);                        // nothing is placed under Terminal until the server offers it
    }
    private static CollectorOperationCapabilityDto Op(string code, CollectorOperationCapabilityStatus status, bool assigned = true, bool? collectible = null) =>
        new(code, code, assigned, status, collectible ?? status == CollectorOperationCapabilityStatus.Ready, []);

    private static TodaysWork Build(
        IReadOnlyList<CollectorOperationCapabilityDto>? ops = null,
        IReadOnlyList<PendingOperation>? queue = null,
        IReadOnlyList<MobileFacilityMenuItemDto>? facilities = null) =>
        TodaysWorkBuilder.Build(facilities ?? [], ops ?? [], queue ?? []);

    [Fact]
    public void A_ready_operation_is_available_and_can_be_opened()
    {
        var work = Build([Op(CollectorOperationCodes.MarketFees, CollectorOperationCapabilityStatus.Ready)]);

        var item = Assert.Single(work.Available);
        Assert.True(item.CanOpen);
        Assert.Equal(WorkTarget.Operation, item.Target);
        Assert.Equal("Ready", item.Status);
        Assert.Empty(work.AssignedUnavailable);
    }

    [Theory]
    [InlineData(CollectorOperationCapabilityStatus.PendingCutover, "Pending activation")]
    [InlineData(CollectorOperationCapabilityStatus.Unsupported, "Not available yet")]
    [InlineData(CollectorOperationCapabilityStatus.AssignedButInactive, "Authorization inactive")]
    [InlineData(CollectorOperationCapabilityStatus.NeedsPolicy, "Collection policy unavailable")]
    public void An_assigned_operation_that_is_not_ready_has_no_collect_action_and_plain_wording(
        CollectorOperationCapabilityStatus status, string wording)
    {
        var work = Build([Op(CollectorOperationCodes.Transportation, status)]);

        Assert.Empty(work.Available);
        var item = Assert.Single(work.AssignedUnavailable);
        Assert.False(item.CanOpen);
        Assert.Equal(WorkTarget.None, item.Target);
        Assert.Equal(wording, item.Status);
    }

    [Fact]
    public void A_missing_accountable_form_is_an_attention_item_and_not_available_work()
    {
        var work = Build([Op(CollectorOperationCodes.LandingBerthing, CollectorOperationCapabilityStatus.NeedsDocument)]);

        Assert.Empty(work.Available);
        Assert.Empty(work.AssignedUnavailable);
        var attention = Assert.Single(work.Attention);
        Assert.Contains("Accountable form required", attention.Text);
        Assert.Equal("form", attention.Kind);
    }

    [Fact]
    public void An_unassigned_operation_does_not_appear_anywhere()
    {
        var work = Build([Op(CollectorOperationCodes.Transportation, CollectorOperationCapabilityStatus.Ready, assigned: false)]);

        Assert.Empty(work.Available);
        Assert.Empty(work.AssignedUnavailable);
        Assert.Empty(work.Attention);
    }

    [Fact]
    public void A_stale_collectible_flag_without_a_ready_status_is_not_trusted()
    {
        var work = Build([Op(CollectorOperationCodes.MarketFees, CollectorOperationCapabilityStatus.PendingCutover, collectible: true)]);

        Assert.Empty(work.Available);
        Assert.Single(work.AssignedUnavailable);
    }

    [Fact]
    public void Ready_WCF_opens_its_own_collection_page_so_an_operation_only_collector_can_collect_it()
    {
        var work = Build([Op(CollectorOperationCodes.Wcf, CollectorOperationCapabilityStatus.Ready)]);

        var item = Assert.Single(work.Available);
        Assert.True(item.CanOpen);
        Assert.Equal(WorkTarget.Operation, item.Target);
        Assert.Equal(CollectorOperationCodes.Wcf, item.OperationCode);
    }

    [Fact]
    public void Queued_failed_and_review_work_shows_as_attention_with_counts_and_synced_work_does_not()
    {
        var queue = new List<PendingOperation>
        {
            new() { LocalStatus = PendingLocalStatus.Pending },
            new() { LocalStatus = PendingLocalStatus.Pending },
            new() { LocalStatus = PendingLocalStatus.Failed },
            new() { LocalStatus = PendingLocalStatus.ReconciliationRequired },
            new() { LocalStatus = PendingLocalStatus.Rejected },
            new() { LocalStatus = PendingLocalStatus.Synced },
        };

        var work = Build(queue: queue);

        Assert.Equal(new[] { "review", "failed", "rejected", "pending" }, work.Attention.Select(x => x.Kind));
        Assert.Equal(2, work.Attention.Single(x => x.Kind == "pending").Count);
        Assert.Equal(1, work.Attention.Single(x => x.Kind == "review").Count);
    }

    [Fact]
    public void Facilities_only_appear_when_assigned_and_only_available_ones_can_open()
    {
        var facilities = new List<MobileFacilityMenuItemDto>
        {
            new(FacilityCode.NPM, "New Public Market", "", IsAssigned: true, IsAvailable: true, BillingArchetype.DailyStall),
            new(FacilityCode.SLH, "Slaughterhouse", "", IsAssigned: true, IsAvailable: false, BillingArchetype.PerHead),
            new(FacilityCode.TCC, "Tabo-an", "", IsAssigned: false, IsAvailable: true, BillingArchetype.MonthlyRental),
        };

        var work = Build(facilities: facilities);

        var open = Assert.Single(work.Available);
        Assert.Equal("New Public Market", open.Name);
        Assert.True(open.CanOpen);
        Assert.Equal("Ready", open.Status);                       // the group already says "Available now"
        var blocked = Assert.Single(work.AssignedUnavailable);
        Assert.Equal("Slaughterhouse", blocked.Name);
        Assert.Equal("Not available yet", blocked.Status);        // a blocked row keeps its real reason
        Assert.DoesNotContain(work.Available.Concat(work.AssignedUnavailable), x => x.Name == "Tabo-an");
    }
}
