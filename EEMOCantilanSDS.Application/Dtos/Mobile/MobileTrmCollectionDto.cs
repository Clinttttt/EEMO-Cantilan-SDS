using EEMOCantilanSDS.Application.Dtos.TransportTerminal;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// Today's transport-terminal collection for a collector: registered transporters (with their
/// trip count today), the day's recorded trips, and running totals. Trips are a flat ₱30 each
/// with an auto-assigned daily trip/queue number.
/// </summary>
public sealed record MobileTrmCollectionDto(
    DateOnly Date,
    decimal TripFee,
    int TripsToday,
    decimal CollectedToday,
    int TransporterCount,
    IReadOnlyList<TrmTransporterListDto> Transporters,
    IReadOnlyList<TrmTripDto> TodayTrips);
