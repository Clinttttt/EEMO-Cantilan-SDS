using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityBillForEntry;

/// <summary>Seed for the utility entry modal: the existing bill (edit) or carry-forward previous readings.</summary>
public record GetUtilityBillForEntryQuery(Guid StallId, int Year, int Month)
    : IRequest<Result<UtilityBillEntryDto>>;
