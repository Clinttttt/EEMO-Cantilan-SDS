using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;

public record RecordDailyCollectionCommand(
    Guid StallId,
    DateOnly CollectionDate,
    bool IsPaid,
    decimal? FishKilos = null,
    string? ORNumber = null
) : IRequest<Result<bool>>;
