using System.Collections.Generic;

namespace EEMOCantilanSDS.Application.Dtos.Audit;

/// <summary>
/// Paged audit-trail result for the admin Audit Trail page. Carries the current page of entries,
/// offset-pagination metadata, action summary counts (for the stat cards), and the distinct
/// actor/entity values used to populate the filter dropdowns.
/// </summary>
/// <remarks>
/// Summary counts (<see cref="TotalEvents"/>, <see cref="CreatedCount"/>, <see cref="UpdatedCount"/>,
/// <see cref="DeletedCount"/>) reflect the search/date/actor/entity scope but ignore the action filter,
/// so the cards always show the full breakdown of the current scope. <see cref="TotalCount"/> reflects
/// ALL active filters (including action) and drives pagination.
/// <para>
/// <see cref="Actors"/> and <see cref="EntityTypes"/> are only populated when options were requested
/// (first load / filter change); on pure pagination they come back empty and the client reuses its cache.
/// </para>
/// </remarks>
public record AuditTrailDto(
    IReadOnlyList<AuditLogDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int TotalEvents,
    int CreatedCount,
    int UpdatedCount,
    int DeletedCount,
    IReadOnlyList<AuditActorOptionDto> Actors,
    IReadOnlyList<string> EntityTypes);
