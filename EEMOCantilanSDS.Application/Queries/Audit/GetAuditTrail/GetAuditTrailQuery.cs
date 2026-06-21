using System;
using EEMOCantilanSDS.Application.Dtos.Audit;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Audit.GetAuditTrail;

public record GetAuditTrailQuery(
    string? Search = null,
    string? Action = null,
    string? EntityType = null,
    string? Actor = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Page = 1,
    int PageSize = 25,
    bool IncludeOptions = true
) : IRequest<Result<AuditTrailDto>>;
