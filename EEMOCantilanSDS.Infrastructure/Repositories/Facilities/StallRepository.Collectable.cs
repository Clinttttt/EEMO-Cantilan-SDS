using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Extensions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of StallRepository: the private arithmetic deciding WHICH DAYS of a month a space is collectable for, and the small
// section labels.
//
// Held in one file on purpose, and shared by the mobile rounds and the printed register. It answers "how much of this month
// does this occupancy owe", which is the figure the office reconciles by hand - two copies is two answers.
public partial class StallRepository
{
    private static DateOnly GetEffectiveCollectionEnd(DateOnly monthStart, DateOnly monthEnd, DateOnly collectionDate)
    {
        if (collectionDate < monthStart)
            return monthStart.AddDays(-1);

        if (collectionDate > monthEnd)
            return monthEnd;

        return collectionDate;
    }

    private static int CountCollectableDays(DateOnly? contractStart, DateOnly monthStart, DateOnly effectiveEnd)
    {
        if (effectiveEnd < monthStart)
            return 0;
        var start = contractStart.HasValue && contractStart.Value > monthStart
            ? contractStart.Value
            : monthStart;

        if (start > effectiveEnd)
            return 0;

        return effectiveEnd.DayNumber - start.DayNumber + 1;
    }

    private static string GetSectionName(MarketSection? section) => section switch
    {
        MarketSection.VegetableArea => "Vegetable Area",
        MarketSection.FishSection => "Fish Area",
        MarketSection.MeatSection => "Meat Area",
        _ => "Unassigned Section"
    };

    /// <summary>
    /// The elapsed days of a month this stall still owes, earliest first.
    ///
    /// <para>
    /// A day is owed when SOME term of the stall covers it - not only the term in force - so days left behind by a lessee
    /// who has since gone can still be collected. Days the market was closed are dropped, because nobody owes them, and so
    /// are days already recorded, whether they were paid or excused.
    /// </para>
    ///
    /// <para>
    /// Deliberately the DATES rather than a count. <c>CountCollectableDays</c> above is a plain span subtraction that knows
    /// nothing about closures or which days are already on record; it answers "how much of this month does this occupancy
    /// owe" for the register. This answers "which days can still be collected", which is a different question and the only
    /// one a collector standing in front of a payor can act on.
    /// </para>
    /// </summary>
    private static List<DateOnly> UncollectedDays(
        Stall stall, DateOnly monthStart, DateOnly effectiveEnd, HashSet<DateOnly> closures)
    {
        var owed = new List<DateOnly>();
        if (effectiveEnd < monthStart) return owed;

        var onRecord = stall.DailyCollections
            .Select(d => d.CollectionDate)
            .ToHashSet();

        for (var date = monthStart; date <= effectiveEnd; date = date.AddDays(1))
        {
            if (closures.Contains(date)) continue;
            if (onRecord.Contains(date)) continue;
            if (!stall.Contracts.Any(c => c.IsCollectableOn(date))) continue;

            owed.Add(date);
        }

        return owed;
    }
}
