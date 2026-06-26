using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.SetMonthlyException;

/// <summary>
/// Marks a monthly-rental stall (TCC/NCC/BBQ/ICE) as excused for a billing month — ₱0 owed, and the
/// month never counts as unpaid/missed/delinquent. Upserts (re-issuing updates the reason/remarks).
/// </summary>
public record SetStallMonthlyExceptionCommand(
    Guid StallId,
    int Year,
    int Month,
    MonthlyExceptionReason Reason = MonthlyExceptionReason.ApprovedByEemo,
    string? Remarks = null
) : IRequest<Result<bool>>;
