using System.Collections.Generic;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.BulkImportStallholders;

/// <summary>
/// Bulk-creates stallholders (stall + active contract) for a facility from an imported list.
/// Best-effort: valid rows are created in a single transaction; invalid rows are reported per-row
/// (the batch is not rejected wholesale). NPM requires a section, applied to every row.
/// </summary>
public record BulkImportStallholdersCommand(
    FacilityCode FacilityCode,
    MarketSection? Section,
    IReadOnlyList<ImportStallRow> Rows) : IRequest<Result<BulkImportResultDto>>;
