using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.ClearMonthlyException;

/// <summary>Removes a monthly excused exception (the month returns to its normal billing state).
/// Idempotent — clearing a month that has no exception succeeds.</summary>
public record ClearStallMonthlyExceptionCommand(Guid StallId, int Year, int Month) : IRequest<Result<bool>>;
