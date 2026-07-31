using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.DailyCollections.GetSettleableNpmDays;

/// <summary>Lists the still-settleable days (unpaid, chargeable to an occupancy, not closed, not future) of one NPM
/// stall for a month — powers the Pay-bill "specific days" picker.</summary>
/// <param name="ContractId">
/// Whose days. On a stall that has been re-let a month can span two lessees, so naming the term keeps one lessee's
/// arrears out of the other's. Omitted for the sitting lessee.
/// </param>
public record GetSettleableNpmDaysQuery(Guid StallId, int Year, int Month, Guid? ContractId = null)
    : IRequest<Result<IReadOnlyList<SettleableNpmDayDto>>>;
