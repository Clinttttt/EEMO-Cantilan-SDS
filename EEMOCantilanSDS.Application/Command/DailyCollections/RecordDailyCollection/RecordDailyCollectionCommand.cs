using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;

public record RecordDailyCollectionCommand(
    Guid StallId,
    DateOnly CollectionDate,
    bool IsPaid,
    decimal? FishKilos = null,
    string? ORNumber = null,
    // Offline-sync idempotency key (set when replaying a queued offline collection); null online.
    Guid? ClientOperationId = null,
    // Excused/absent day: the payor was not operating. ₱0 owed, mutually exclusive with IsPaid.
    bool IsAbsent = false
) : IRequest<Result<bool>>;
