using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetStallCollectionHistory;

/// <summary>
/// Cursor-paginated transparency log of a stall's collections (newest first). Cursor is the date of
/// the last row from the previous page (null for the first page).
/// </summary>
public record GetStallCollectionHistoryQuery(
    Guid StallId,
    DateTime? Cursor = null,
    int PageSize = 10) : IRequest<Result<CursorPagedResult<StallCollectionHistoryRowDto>>>;
