using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.BulkImportDailyHistory;

/// <summary>
/// Records an office's existing collection history for a daily-billed facility, one row per month per space.
///
/// <para>
/// The monthly import cannot serve the market. A month there is not a payment; it is a run of market days at a fixed
/// daily fee, and recording a month's worth of days as one monthly payment would settle days nobody collected and
/// leave the derived arrears disagreeing with the collection sheet it came from.
/// </para>
///
/// <para>
/// So the row states DAYS, and the import settles that many collectable days in the month through the same rules the
/// market's own collection dialog uses: never a future day, never a day no term answers for, never a facility-wide
/// closure, never a day already collected or excused. The fee for each day is the facility's own rate on that date,
/// so a mid-year change in the ordinance is honoured rather than averaged away.
/// </para>
///
/// <para>Valid rows are recorded in one transaction; a row that cannot be recorded in full is reported with the
/// reason and whatever it did settle, rather than rejecting the batch.</para>
/// </summary>
public record BulkImportDailyHistoryCommand(
    FacilityCode FacilityCode,
    MarketSection? Section,
    IReadOnlyList<ImportDailyPaymentRow> Rows,
    string? CustomSectionName = null) : IRequest<Result<BulkImportDailyResultDto>>;
