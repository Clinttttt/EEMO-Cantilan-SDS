using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;

/// <summary>
/// Records specific NPM days of one stall as PAID in a single transaction, optionally stamping ONE
/// Official Receipt across all of them (one physical receipt covering the selected days). Days that are
/// future, market-closed, already paid/absent, or that no term answers for are skipped.
/// </summary>
public record SettleNpmDaysCommand(
    Guid StallId,
    IReadOnlyList<DateOnly> Dates,
    string? ORNumber) : IRequest<Result<bool>>;
