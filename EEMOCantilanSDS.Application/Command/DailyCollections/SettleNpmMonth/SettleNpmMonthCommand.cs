using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;

/// <summary>
/// Settles a whole NPM (daily) month in one action: marks every collectable, not-yet-paid, non-future
/// day in the month as collected (at the day's resolved rate). This lets the office record a monthly
/// payment for a daily-collected market stall through a simple formal form — no per-day clicking — while
/// keeping the ledger daily-truth. An optional OR/receipt number is stamped on the month's last settled
/// day (mirroring the existing NPM Add-OR pattern).
/// </summary>
public record SettleNpmMonthCommand(
    Guid StallId,
    int Year,
    int Month,
    string? ORNumber = null
) : IRequest<Result<bool>>;
