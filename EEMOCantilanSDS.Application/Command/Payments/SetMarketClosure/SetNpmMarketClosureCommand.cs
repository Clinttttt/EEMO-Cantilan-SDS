using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;

/// <summary>Closes the whole NPM market for a day — every NPM payor is excused for that date.
/// Upserts (re-issuing updates the reason/remarks).</summary>
public record SetNpmMarketClosureCommand(
    DateOnly Date,
    MarketClosureReason Reason = MarketClosureReason.ApprovedByEemo,
    string? Remarks = null
) : IRequest<Result<bool>>;
