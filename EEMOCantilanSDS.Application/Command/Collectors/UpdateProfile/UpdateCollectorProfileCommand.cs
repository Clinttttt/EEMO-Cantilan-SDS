using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.UpdateProfile;

/// <summary>
/// Self-service profile update for the authenticated collector. The collector id is resolved from
/// the token (never the request); Employee ID, username and assignments are admin-managed and are
/// not editable here.
/// </summary>
public sealed record UpdateCollectorProfileCommand(
    string FullName,
    string ContactNumber,
    string Email) : IRequest<Result<bool>>;
