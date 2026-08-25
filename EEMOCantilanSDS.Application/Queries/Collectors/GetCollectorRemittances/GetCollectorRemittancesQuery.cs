using EEMOCantilanSDS.Application.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorRemittances;

/// <summary>
/// One collector's remittances over a period, with the position they add up to. Read by the Report of Collections and by
/// the office screen that files them, so both state the same three figures.
/// </summary>
public sealed record GetCollectorRemittancesQuery(Guid CollectorId, DateOnly From, DateOnly To)
    : IRequest<Result<CollectorRemittanceSummaryDto>>;

/// <param name="FeeCollections">
/// Fee money the collector took in the period. Electricity and water are banked separately as additional income and are
/// not part of this figure, nor of what may be remitted against it.
/// </param>
/// <param name="NotYetRemitted">
/// What the collector still holds for the period. Exact rather than indicative, because two remittances may not cover the
/// same day.
/// </param>
public sealed record CollectorRemittanceSummaryDto(
    Guid CollectorId,
    DateOnly From,
    DateOnly To,
    decimal FeeCollections,
    decimal Remitted,
    decimal NotYetRemitted,
    IReadOnlyList<CollectorRemittanceLineDto> Remittances);

public sealed record CollectorRemittanceLineDto(
    Guid Id,
    DateTime ReceivedAt,
    decimal Amount,
    DateOnly CoversFrom,
    DateOnly CoversTo,
    string ReceivedByName,
    string? ReferenceNo,
    string? Notes);
