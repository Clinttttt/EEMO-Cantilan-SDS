using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.BulkImportPaymentHistory;

/// <summary>
/// Records an office's existing payment history for a monthly-billed facility, one row per month per space.
///
/// <para>
/// The office adopts the system mid-contract and does not want to wait for its terms to run out. Importing the
/// HISTORY rather than an opening balance means arrears stay derived: what a payor owes goes on being worked out
/// from the term and the payments against it, so there is no second figure to trust and every peso traces to a
/// receipt. It also needs no new concept anywhere - these become ordinary payment records, and every screen that
/// already reads them works untouched.
/// </para>
///
/// <para>
/// Monthly-billed facilities only. The market bills per market day, so its history is daily collections rather
/// than one row per month; a single template covering both would quietly mis-record it. That is deliberately a
/// separate job.
/// </para>
///
/// <para>Valid rows are recorded in one transaction; a row that cannot be recorded is reported with the reason
/// rather than rejecting the batch, matching the stallholder import.</para>
/// </summary>
public record BulkImportPaymentHistoryCommand(
    FacilityCode FacilityCode,
    MarketSection? Section,
    IReadOnlyList<ImportPaymentRow> Rows,
    string? CustomSectionName = null) : IRequest<Result<BulkImportPaymentResultDto>>;
