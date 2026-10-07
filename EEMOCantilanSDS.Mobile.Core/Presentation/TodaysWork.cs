using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Mobile.Models;

namespace EEMOCantilanSDS.Mobile.Presentation;

/// <summary>Plain-language wording for a server capability status. The internal code is never shown to a collector.</summary>
public static class CapabilityWording
{
    public static string For(CollectorOperationCapabilityStatus status) => status switch
    {
        CollectorOperationCapabilityStatus.Ready => "Ready to collect",
        CollectorOperationCapabilityStatus.Unsupported => "Not available yet",
        CollectorOperationCapabilityStatus.AssignedButInactive => "Authorization inactive",
        CollectorOperationCapabilityStatus.PendingCutover => "Pending activation",
        CollectorOperationCapabilityStatus.NeedsPolicy => "Collection policy unavailable",
        CollectorOperationCapabilityStatus.NeedsDocument => "Accountable form required",
        _ => "Not available",
    };
}

public enum WorkTarget { None, Facility, Operation }

/// <summary>One row in Today's Work. <see cref="CanOpen"/> is true only for something that is ready to collect.</summary>
public sealed record WorkItem(string Name, string Status, WorkTarget Target, bool CanOpen, string? FacilityCode = null, string? OperationCode = null,
    CollectionFamily? Family = null);

public sealed record WorkSection(string Name, IReadOnlyList<WorkItem> Items);

/// <summary>
/// Groups already-authorized rows under the office's Monthly Income families, without deciding availability or routing. The
/// placement comes from the shared official structure and revenue-source catalog, never from the old facility ownership.
/// </summary>
public static class WorkSections
{
    public const string Market = "Income from Market";
    public const string Rent = "Rent Income (Stall Rental)";
    public const string Space = "Space Rental";
    public const string Terminal = "Income from Terminal";
    public const string Slaughterhouse = "Income from Slaughterhouse";
    public const string Other = "Other operations";

    public static IReadOnlyList<WorkSection> Group(IReadOnlyList<WorkItem> available) =>
        new[] { Market, Rent, Space, Terminal, Slaughterhouse, Other }
            .Select(name => new WorkSection(name, available.Where(item => Section(item) == name).ToList()))
            .Where(section => section.Items.Count > 0).ToList();

    private static string Section(WorkItem item)
    {
        // The server states an operation's official family; a facility, which has none, is placed by the official structure below.
        if (item.Family is { } stated) return Family(stated);
        if (Enum.TryParse<EEMOCantilanSDS.Domain.Enums.FacilityCode>(item.FacilityCode, out var facility))
        {
            // A rent facility is placed by the official statement's own row for it (NPM, NCC, TCC, BBQ).
            var row = OfficialMonthlyIncomeStructure.Rows.FirstOrDefault(r => r.Facility == facility);
            if (row is not null) return Family(RevenueSourceCatalog.For(row.Key).GroupKey);
            if (facility == EEMOCantilanSDS.Domain.Enums.FacilityCode.ICE) return Family(RevenueSourceCatalog.For("ICE_PLANT").GroupKey);
            if (facility == EEMOCantilanSDS.Domain.Enums.FacilityCode.TPM) return Family(RevenueSourceCatalog.For("TABO").GroupKey);
            if (facility == EEMOCantilanSDS.Domain.Enums.FacilityCode.SLH) return Family(RevenueSourceCatalog.For("SLAUGHTERHOUSE").GroupKey);
            if ((int)facility >= 101) return Rent;
            return Other;
        }
        // The operation permission and the official row share a name except Transportation / Parking.
        var key = item.OperationCode == CollectorOperationCodes.Transportation ? "TRANSPORTATION_PARKING" : item.OperationCode;
        return key is not null && RevenueSourceCatalog.Knows(key) ? Family(RevenueSourceCatalog.For(key).GroupKey) : Other;
    }

    private static string Family(CollectionFamily family) => family switch
    {
        CollectionFamily.Rent => Rent,
        CollectionFamily.Space => Space,
        CollectionFamily.Terminal => Terminal,
        CollectionFamily.Slaughterhouse => Slaughterhouse,
        _ => Market
    };

    private static string Family(string groupKey) => groupKey switch
    {
        RevenueSourceCatalog.MarketGroup => Market,
        RevenueSourceCatalog.RentGroup => Rent,
        RevenueSourceCatalog.SpaceGroup => Space,
        RevenueSourceCatalog.SlaughterhouseGroup => Slaughterhouse,
        _ => Other
    };
}

/// <summary>Something that needs the collector's attention: queued, failed or review-required work, or a missing form.</summary>
public sealed record AttentionItem(string Text, int Count, string Kind);

/// <summary>What the Menu shows: work that can be collected now, assigned work that cannot, and things needing attention.</summary>
public sealed record TodaysWork(
    IReadOnlyList<WorkItem> Available,
    IReadOnlyList<WorkItem> AssignedUnavailable,
    IReadOnlyList<AttentionItem> Attention);

public static class TodaysWorkBuilder
{
    /// <summary>
    /// The chip on a row under "Available now". The group heading already says it can be collected, so the chip is just
    /// "Ready". Presentation copy only: availability is still the server's Ready status.
    /// </summary>
    public const string ReadyLabel = "Ready";

    /// <summary>
    /// Assignment is not collectibility. Only a facility the server marks available, or an operation the server reports Ready,
    /// is offered for collection. Unassigned work does not appear at all. A missing accountable form is an attention item.
    /// </summary>
    public static TodaysWork Build(
        IReadOnlyList<MobileFacilityMenuItemDto> facilities,
        IReadOnlyList<CollectorOperationCapabilityDto> operations,
        IReadOnlyList<PendingOperation> queue)
    {
        var available = new List<WorkItem>();
        var unavailable = new List<WorkItem>();
        var attention = new List<AttentionItem>();

        foreach (var f in facilities.Where(x => x.IsAssigned))
        {
            if (f.IsAvailable)
                available.Add(new WorkItem(f.Name, ReadyLabel, WorkTarget.Facility, true, FacilityCode: f.Code.ToString()));
            else
                unavailable.Add(new WorkItem(f.Name, "Not available yet", WorkTarget.None, false));
        }

        foreach (var op in operations.Where(x => x.IsAssigned))
        {
            // A Ready status is the only way to collect; a stale IsCollectible flag with any other status is not trusted.
            var ready = op.Status == CollectorOperationCapabilityStatus.Ready && op.IsCollectible;
            if (ready)
            {
                // WCF is a utility operation (IA-053) with its own collection page, so an operation-only collector can open it;
                // a collector who also holds the market can still collect it from the stall sheet.
                var opens = op.OperationCode is CollectorOperationCodes.Wcf or CollectorOperationCodes.Terminal or CollectorOperationCodes.FishMeatVendorFee or CollectorOperationCodes.WeightAndMeasure
                    || GovernedServiceCatalog.Find(op.OperationCode) is not null;
                available.Add(new WorkItem(op.Name, ReadyLabel, opens ? WorkTarget.Operation : WorkTarget.None, opens, OperationCode: op.OperationCode, Family: op.Family));
            }
            else if (op.Status == CollectorOperationCapabilityStatus.NeedsDocument)
                attention.Add(new AttentionItem($"{op.Name}: {CapabilityWording.For(op.Status)}", 1, "form"));
            else
                unavailable.Add(new WorkItem(op.Name, CapabilityWording.For(op.Status), WorkTarget.None, false, OperationCode: op.OperationCode));
        }

        Add(attention, queue.Count(x => x.LocalStatus == PendingLocalStatus.ReconciliationRequired), "needs office review", "review");
        Add(attention, queue.Count(x => x.LocalStatus == PendingLocalStatus.Failed), "failed to sync", "failed");
        Add(attention, queue.Count(x => x.LocalStatus == PendingLocalStatus.Rejected), "rejected", "rejected");
        Add(attention, queue.Count(x => x.LocalStatus == PendingLocalStatus.Pending), "waiting to sync", "pending");

        return new TodaysWork(available, unavailable, attention);
    }

    private static void Add(List<AttentionItem> list, int count, string text, string kind)
    {
        if (count > 0) list.Add(new AttentionItem(text, count, kind));
    }
}
