namespace EEMOCantilanSDS.Domain.Entities.Facilities;

/// <summary>
/// One period during which a lessee held a physical stall: the contract, and the days it actually covered.
///
/// <para>A stall outlives its lessees, so history is a sequence of these. Money is attributed to the occupancy
/// whose window contains the BILLING period it was raised for — never the day it happened to be received — so an
/// arrear settled long after a handover still belongs to the lessee who incurred it.</para>
/// </summary>
/// <param name="Contract">The contract for this period (occupant, rate, term).</param>
/// <param name="Start">First day of the occupancy.</param>
/// <param name="End">
/// Last day the lessee actually held the stall: terminated, superseded by the next lessee, frozen by closure, or
/// the term's end. Money RECEIVED is attributed within this window — including money received while holding on a
/// lapsed contract, which is real money from that lessee.
/// </param>
/// <param name="BillableEnd">
/// Last day this occupancy could be CHARGED for: never past the contract's term. A lessee who stayed on after
/// their term lapsed owes nothing for the lapsed days — there was no contract to bill against — which is the rule
/// the register and the renew path have always followed.
/// </param>
/// <param name="IsCurrent">True when this is the occupancy in force.</param>
public sealed record StallOccupancy(Contract Contract, DateOnly Start, DateOnly End, DateOnly BillableEnd, bool IsCurrent)
{
    /// <summary>The lessee as the office records them.</summary>
    public string Occupant => Contract.ActualOccupant;

    /// <summary>True when this occupancy covered any part of the given period.</summary>
    public bool Overlaps(DateOnly periodStart, DateOnly periodEnd) => Start <= periodEnd && periodStart <= End;

    /// <summary>
    /// The one occupancy answerable for a monthly billing period. A month's charge is a single, indivisible
    /// obligation here — one payment record per stall per month — so a stall handed over mid-month is answered for
    /// by the lessee whose occupancy began latest within it. This is the rule of record: the register, the reports
    /// and the collection dialog all read it, which is what stops a handover month being billed to, or credited
    /// against, two lessees at once. Null when no occupancy covered the month.
    /// </summary>
    public static StallOccupancy? AnsweringForMonth(IEnumerable<StallOccupancy> windows, int year, int month)
    {
        if (month is < 1 or > 12) return null;

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        StallOccupancy? answering = null;
        foreach (var window in windows)
        {
            if (window.Start > monthEnd || monthStart > window.End) continue;
            if (answering is null || window.Start > answering.Start) answering = window;
        }

        return answering;
    }

    /// <summary>The days of the given period that fall inside this occupancy (for daily-billed facilities).</summary>
    public (DateOnly From, DateOnly To)? ClampTo(DateOnly periodStart, DateOnly periodEnd)
    {
        var from = Start > periodStart ? Start : periodStart;
        var to = End < periodEnd ? End : periodEnd;
        return from <= to ? (from, to) : null;
    }
}
