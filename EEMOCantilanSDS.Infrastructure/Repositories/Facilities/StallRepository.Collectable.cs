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
}
