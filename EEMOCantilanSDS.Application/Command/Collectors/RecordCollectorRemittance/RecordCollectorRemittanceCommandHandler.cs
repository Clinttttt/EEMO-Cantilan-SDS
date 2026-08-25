using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.RecordCollectorRemittance;

/// <summary>
/// Files a remittance and answers with the position it creates.
///
/// <para>
/// Nothing else in the system moves because of this. A remittance is a record of custody, so no fee, balance, collection
/// rate or facility report is touched: those say what was collected, this says what has since been handed in. That is a
/// property worth stating, because the temptation with a money record is to let it adjust the figures it refers to.
/// </para>
/// </summary>
public class RecordCollectorRemittanceCommandHandler(
    ICollectorRemittanceRepository remittances,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<RecordCollectorRemittanceCommand, Result<RemittanceRecordedDto>>
{
    public async Task<Result<RemittanceRecordedDto>> Handle(
        RecordCollectorRemittanceCommand request, CancellationToken ct)
    {
        // The office side receives the money. A collector recording their own remittance would defeat the record: its whole
        // purpose is that somebody else took the cash and said so.
        if (currentUser.CollectorId is not null)
            return Result<RemittanceRecordedDto>.Forbidden();

        if (currentUser.UserId is not { } officerId || string.IsNullOrWhiteSpace(currentUser.Username))
            return Result<RemittanceRecordedDto>.Forbidden();

        var remittance = CollectorRemittance.Create(
            request.CollectorId,
            request.Amount,
            // Stored as an instant. A time the office states is given in their own wall clock, so it is converted.
            request.ReceivedAt is { } stated ? PhilippineTime.ToUtcFromPhilippine(stated) : clock.UtcNow,
            request.CoversFrom,
            request.CoversTo,
            officerId,
            currentUser.Username!,
            request.ReferenceNo,
            request.Notes,
            currentUser.Username!);

        await remittances.AddAsync(remittance, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Read back the position over the covered days, so the officer sees what they have just established.
        var collected = await remittances.GetFeeCollectionsTotalAsync(request.CollectorId, request.CoversFrom, request.CoversTo, ct);
        var remitted = await remittances.GetRemittedTotalAsync(request.CollectorId, request.CoversFrom, request.CoversTo, ct);

        return Result<RemittanceRecordedDto>.Success(new RemittanceRecordedDto(
            remittance.Id,
            remittance.Amount,
            collected,
            remitted,
            collected - remitted,
            string.IsNullOrWhiteSpace(remittance.ReferenceNo)));
    }
}
