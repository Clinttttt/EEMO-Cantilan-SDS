using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;

public class RecordDailyCollectionCommandHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    IStallRepository stallRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<RecordDailyCollectionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordDailyCollectionCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null)
            return Result<bool>.NotFound();

        var existing = await dailyCollectionRepository.GetByStallAndDateAsync(request.StallId, request.CollectionDate, ct);

        if (existing is not null)
        {
            if (request.IsPaid)
            {
                existing.MarkPaid(
                    orNumber: string.Empty,
                    collectorId: Guid.Empty,
                    fishKilos: request.FishKilos,
                    updatedBy: "System");
            }
            else
            {
                existing.MarkUnpaid("System");
            }
        }
        else
        {
            var newCollection = DailyCollection.Create(
                stallId: request.StallId,
                collectionDate: request.CollectionDate,
                createdBy: "System");

            if (request.IsPaid)
            {
                newCollection.MarkPaid(
                    orNumber: string.Empty,
                    collectorId: Guid.Empty,
                    fishKilos: request.FishKilos,
                    updatedBy: "System");
            }

            await dailyCollectionRepository.AddAsync(newCollection, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
