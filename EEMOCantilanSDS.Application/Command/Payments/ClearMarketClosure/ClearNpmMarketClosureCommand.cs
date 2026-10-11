using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;

/// <summary>
/// Opens the market for a day. An explicit closure is removed; a day the weekly schedule closes is opened by an explicit
/// opening instead, so the schedule is never edited for a single day. A past day needs the Head and a reason.
/// </summary>
public record ClearNpmMarketClosureCommand(DateOnly Date, string? Remarks = null) : IRequest<Result<bool>>;
