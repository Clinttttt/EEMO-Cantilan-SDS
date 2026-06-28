using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.SaveSlaughterOrNumber;

public class SaveSlaughterOrNumberCommandHandler(
    ISlaughterRepository slaughterRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<SaveSlaughterOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveSlaughterOrNumberCommand request, CancellationToken ct)
    {
        // All blank-OR rows for this visit (owner + date) — one receipt may span several animal rows.
        var rows = await slaughterRepository.GetUnreceiptedByOwnerDateAsync(request.OwnerName, request.TransactionDate, ct);
        if (rows.Count == 0)
            return Result<bool>.NotFound();

        var or = request.ORNumber.Trim();
        var by = currentUser.Username ?? "Admin";

        // One official receipt covers the whole visit: stamp the same OR on every animal row.
        foreach (var transaction in rows)
            transaction.SetOrNumber(or, by);

        await unitOfWork.SaveChangesAsync(ct);   // rows are tracked
        return Result<bool>.Success(true);
    }
}
