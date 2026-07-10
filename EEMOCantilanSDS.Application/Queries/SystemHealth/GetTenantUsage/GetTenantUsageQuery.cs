using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.SystemHealth.GetTenantUsage;

/// <summary>Storage footprint for the authenticated caller's own municipality (tenant-scoped).</summary>
public record GetTenantUsageQuery : IRequest<Result<TenantUsageDto>>;
