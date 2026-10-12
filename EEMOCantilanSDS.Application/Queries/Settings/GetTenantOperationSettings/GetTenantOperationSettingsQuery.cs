using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Settings.GetTenantOperationSettings;

/// <summary>The caller's own municipality's operation directory configuration. Tenant-scoped; no municipality is taken from the request.</summary>
public sealed record GetTenantOperationSettingsQuery : IRequest<Result<TenantOperationSettingsDto>>;
