using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.DailyCollections.GetSettleableNpmDays;

/// <summary>Lists the still-settleable days (unpaid, under contract, not closed, not future) of one NPM
/// stall for a month — powers the Pay-bill "specific days" picker.</summary>
public record GetSettleableNpmDaysQuery(Guid StallId, int Year, int Month)
    : IRequest<Result<IReadOnlyList<SettleableNpmDayDto>>>;
