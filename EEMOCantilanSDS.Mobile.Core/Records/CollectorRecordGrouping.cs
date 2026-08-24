using EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Records;

/// <summary>
/// How the mobile Records feed turns collection records into the entries a collector reads.
///
/// <para>
/// One entry per payor at one stall for the day, however many receipts they were given. A payor who paid in the morning
/// and again in the evening, or whose owed days were cleared on separate receipts, used to appear as two or three cards
/// carrying the same name, and the collector had to add them up by eye. Each receipt is kept intact inside the entry, so
/// the merge never hides a receipt number: the detail names every one of them against its own time and amount.
/// </para>
///
/// <para>
/// Three things are deliberately never merged. An absence is a ₱0 statement rather than a payment. An office-recorded
/// entry stays apart from the collector's own, so attribution is plain. And two stalls of one payor stay apart, because
/// the office reads a stall.
/// </para>
///
/// <para>
/// This lives outside the razor page so the arithmetic a collector reconciles cash against can be tested. The mobile UI
/// itself has no automated coverage.
/// </para>
/// </summary>
public static class CollectorRecordGrouping
{
    /// <summary>Groups one day's records into entries, preserving the order the records arrived in.</summary>
    public static IReadOnlyList<CollectorRecordEntry> Build(IEnumerable<MobileCollectorRecordDto> records)
    {
        var entries = new List<CollectorRecordEntry>();
        var byPayor = new Dictionary<string, CollectorRecordEntry>();

        foreach (var r in records)
        {
            if (r.IsAbsent)
            {
                entries.Add(new CollectorRecordEntry(r));
                continue;
            }

            var key = string.Join("|", r.FacilityCode, r.PayorName, r.StallNo ?? string.Empty, r.IsAdminRecorded);
            if (byPayor.TryGetValue(key, out var existing))
            {
                existing.Add(r);
                continue;
            }

            var entry = new CollectorRecordEntry(r);
            byPayor[key] = entry;
            entries.Add(entry);
        }

        return entries;
    }
}

/// <summary>One card on the Records feed: a payor's collections at one stall for the day, receipts kept distinct.</summary>
public sealed class CollectorRecordEntry
{
    private readonly List<MobileCollectorRecordDto> _items;

    public CollectorRecordEntry(MobileCollectorRecordDto first) => _items = [first];

    internal void Add(MobileCollectorRecordDto record) => _items.Add(record);

    public IReadOnlyList<MobileCollectorRecordDto> Items => _items;

    /// <summary>The record whose shared facts (facility, stall, area, payor) describe the whole entry.</summary>
    public MobileCollectorRecordDto Primary => _items[0];

    public int Count => _items.Count;

    public bool IsMerged => _items.Count > 1;

    /// <summary>What the payor handed over. The card states this, never one payment standing for the rest.</summary>
    public decimal TotalAmount => _items.Sum(i => i.Amount);

    public decimal TotalPaid => _items.Sum(i => i.AmountPaid);

    public bool AnyPartial => _items.Any(i => i.IsPartial) || TotalPaid < TotalAmount;

    /// <summary>The receipts behind this entry, earliest first, each holding the payments it covered.</summary>
    public IReadOnlyList<IGrouping<string, MobileCollectorRecordDto>> Receipts =>
        _items.GroupBy(i => i.ORNumber)
              .OrderBy(g => g.Min(i => i.CollectedAt))
              .ToList();

    public int ReceiptCount => _items.Select(i => i.ORNumber).Distinct().Count();

    public DateTime LatestAt => _items.Max(i => i.CollectedAt);

    /// <summary>False where the entry mixes kinds of charge, in which case the card states no single nature.</summary>
    public bool SharesOneNature => _items.Select(i => i.Nature).Distinct().Count() == 1;
}
