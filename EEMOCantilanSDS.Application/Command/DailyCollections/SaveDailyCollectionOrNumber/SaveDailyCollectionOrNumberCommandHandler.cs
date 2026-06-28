using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;

public class SaveDailyCollectionOrNumberCommandHandler(
    IDailyCollectionRepository dailyCollectionRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<SaveDailyCollectionOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveDailyCollectionOrNumberCommand request, CancellationToken ct)
    {
        var collection = await dailyCollectionRepository.GetByStallAndDateAsync(request.StallId, request.CollectionDate, ct);

        // Only an existing PAID day can be receipted; an unpaid/absent day has nothing to OR.
        if (collection is null || !collection.IsPaid)
            return Result<bool>.NotFound();

        collection.SetOrNumber(request.ORNumber.Trim(), currentUser.Username ?? "Admin");
        await unitOfWork.SaveChangesAsync(ct);   // GetByStallAndDateAsync returns a tracked entity

        return Result<bool>.Success(true);
    }
}
