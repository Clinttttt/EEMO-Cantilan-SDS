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
public sealed record WorkItem(string Name, string Status, WorkTarget Target, bool CanOpen, string? FacilityCode = null, string? OperationCode = null);

public sealed record WorkSection(string Name, IReadOnlyList<WorkItem> Items);

/// <summary>Groups already-authorized rows without deciding availability or routing.</summary>
public static class WorkSections
{
    public static IReadOnlyList<WorkSection> Group(IReadOnlyList<WorkItem> available) =>
        new[] { "Rent & space", "Market & vendor", "Utilities", "Transport & services", "Other operations" }
            .Select(name => new WorkSection(name, available.Where(item => Section(item) == name).ToList()))
            .Where(section => section.Items.Count > 0).ToList();

    private static string Section(WorkItem item)
    {
        if (Enum.TryParse<EEMOCantilanSDS.Domain.Enums.FacilityCode>(item.FacilityCode, out var facility))
        {
            if (facility is EEMOCantilanSDS.Domain.Enums.FacilityCode.TCC or EEMOCantilanSDS.Domain.Enums.FacilityCode.NCC
                or EEMOCantilanSDS.Domain.Enums.FacilityCode.BBQ or EEMOCantilanSDS.Domain.Enums.FacilityCode.ICE || (int)facility >= 101)
                return "Rent & space";
            if (facility is EEMOCantilanSDS.Domain.Enums.FacilityCode.NPM or EEMOCantilanSDS.Domain.Enums.FacilityCode.TPM)
                return "Market & vendor";
            if (facility is EEMOCantilanSDS.Domain.Enums.FacilityCode.TRM or EEMOCantilanSDS.Domain.Enums.FacilityCode.SLH)
                return "Transport & services";
        }
        return item.OperationCode switch
        {
            CollectorOperationCodes.Wcf => "Utilities",
            CollectorOperationCodes.MarketFees or CollectorOperationCodes.VegetableFruitSpaceRental or CollectorOperationCodes.Tabo => "Market & vendor",
            CollectorOperationCodes.Transportation or CollectorOperationCodes.LandingBerthing
                or CollectorOperationCodes.TransferLargeCattle or CollectorOperationCodes.Slaughterhouse => "Transport & services",
            _ => "Other operations"
        };
    }
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
                var opens = op.OperationCode == CollectorOperationCodes.Wcf || GovernedServiceCatalog.Find(op.OperationCode) is not null;
                available.Add(new WorkItem(op.Name, ReadyLabel, opens ? WorkTarget.Operation : WorkTarget.None, opens, OperationCode: op.OperationCode));
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
