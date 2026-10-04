using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;

/// <summary>
/// The unified Collection Activity for an inclusive business-date period of at most <see cref="MaxDays"/> days. Optional
/// filters narrow the events (and the totals) to one facility, one collector or one authority ("Legacy"/"Canonical").
/// </summary>
public sealed record GetCollectionActivityQuery(
    DateOnly From,
    DateOnly To,
    FacilityCode? Facility = null,
    Guid? CollectorId = null,
    string? Authority = null,
    int Limit = GetCollectionActivityQuery.DefaultLimit,
    // Exact StallTrack Reference Code lookup (case-insensitive). When set, From/To are ignored and the result is the one canonical
    // Collection with that SRC, on its own business date; no SRC is ever matched to a legacy row.
    string? Reference = null) : IRequest<Result<CollectionActivityFeedDto>>
{
    public const int MaxDays = 31;
    public const int DefaultLimit = 500;
    public const int MaxLimit = 5000;
}
