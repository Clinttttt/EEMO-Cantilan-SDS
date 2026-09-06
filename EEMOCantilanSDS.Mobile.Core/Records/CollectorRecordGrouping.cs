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
/// Two things are deliberately never merged. An office-recorded entry stays apart from the collector's own, so attribution
/// is plain. And two stalls of one payor stay apart, because the office reads a stall. An absence stays apart from a
/// PAYMENT for the same reason it always did - it is a ₱0 statement, not money - but several excused days for one payor at
/// one stall are one card, because a section closure excuses a whole span in a single act.
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
            // An absence never merges with a PAYMENT - it is a ₱0 statement, not money - but absences for one payor at one
            // stall do merge with each other. Until 2026-09-06 each was its own card, which was right while an absence meant
            // a collector marking one stall on one day. It stopped being right when a section closure began excusing a whole
            // frozen span at once: the office closed Sari Sari, six days were excused in one act, and the feed showed six
            // identical cards for the same payor, every one stamped with the moment they were written. The kind is part of
            // the key, so the two never collapse into one another.
            var key = string.Join("|", r.FacilityCode, r.PayorName, r.StallNo ?? string.Empty, r.IsAdminRecorded, r.IsAbsent);
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

    /// <summary>
    /// The days this entry is FOR, earliest first — not the day it was written.
    /// </summary>
    /// <remarks>
    /// A closure excuses a whole frozen span in one act, so every day of it carries the same written-at time. Stating that
    /// time tells the office nothing and suggests the excusal happened on a day nobody touched the section. The day being
    /// settled is the fact worth reading.
    /// </remarks>
    public IReadOnlyList<DateOnly> FeeDays =>
        _items.Where(i => i.FeeDate is not null)
              .Select(i => i.FeeDate!.Value)
              .Distinct()
              .OrderBy(d => d)
              .ToList();

    /// <summary>
    /// The days this entry covers, written the way the office reads a date range.
    /// </summary>
    /// <remarks>
    /// One day is named outright. A run of days is given as a span, because a closure of a fortnight would otherwise fill
    /// the card with dates. Empty for facilities that carry no per-day fee, where the caller states its own period.
    /// </remarks>
    public string FeeDayLabel
    {
        get
        {
            var days = FeeDays;
            if (days.Count == 0) return string.Empty;
            if (days.Count == 1) return days[0].ToString("MMM d");

            var first = days[0];
            var last = days[^1];

            return first.Month == last.Month
                ? $"{first:MMM d}–{last.Day}"
                : $"{first:MMM d} – {last:MMM d}";
        }
    }
}
