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
    Guid? ClientOperationId = null
) : IRequest<Result<bool>>;
