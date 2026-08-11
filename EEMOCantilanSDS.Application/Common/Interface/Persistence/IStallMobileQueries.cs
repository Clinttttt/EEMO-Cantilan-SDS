using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// What the collector's app shows for one round of collection.
///
/// <para>
/// A collector on the floor needs a different shape from anything the office reads: every payor they will meet today, in
/// one payload, already carrying today's status and what is owed — because the app must keep working when the signal
/// drops halfway down the market. These are two whole-screen projections, not a stall lookup repeated in a loop.
/// </para>
///
/// <para>
/// Split out of <see cref="IStallRepository"/>, which also loads stalls as aggregates to be modified, checks number
/// uniqueness and answers the office's registers and reports. The mobile app has no business with any of that, and a
/// handler serving it should not be able to reach them.
/// </para>
/// </summary>
public interface IStallMobileQueries
{
    /// <summary>The market's collection screen for one date: every daily-billed space and its status that day.</summary>
    Task<MobileNpmCollectionDto> GetMobileNpmCollectionAsync(
        int year, int month, DateOnly collectionDate, CancellationToken ct);

    /// <summary>A monthly-billed facility's collection screen for one period, with each account's balance.</summary>
    Task<MobileMonthlyCollectionDto> GetMobileMonthlyCollectionAsync(
        FacilityCode facilityCode, int year, int month, DateOnly collectionDate, CancellationToken ct);
}
