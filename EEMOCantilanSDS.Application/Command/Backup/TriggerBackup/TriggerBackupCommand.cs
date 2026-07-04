using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Backup.TriggerBackup;

/// <summary>
/// Head-triggered on-demand backup: dispatches the GitHub Actions backup workflow.
/// No user input — attribution/authorization is enforced at the API (SuperAdmin only).
/// </summary>
public record TriggerBackupCommand : IRequest<Result<bool>>;
