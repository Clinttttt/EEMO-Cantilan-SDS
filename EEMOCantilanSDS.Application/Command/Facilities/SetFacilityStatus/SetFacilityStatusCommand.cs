using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetFacilityStatus;

/// <summary>
/// Activates or deactivates a facility for the CURRENT tenant. Deactivating hides it from the operational
/// menus (sidebar, dashboard) but preserves all history — records are never deleted, and the facility can
/// be reactivated. Soft, reversible, and label-only from the billing machinery's point of view.
/// </summary>
public record SetFacilityStatusCommand(string Code, bool Active) : IRequest<Result<bool>>;
