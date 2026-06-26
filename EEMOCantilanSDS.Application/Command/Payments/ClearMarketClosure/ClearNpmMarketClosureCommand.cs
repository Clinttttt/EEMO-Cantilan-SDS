using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;

/// <summary>Reopens the NPM market for a day (removes the closure). Idempotent.</summary>
public record ClearNpmMarketClosureCommand(DateOnly Date) : IRequest<Result<bool>>;
