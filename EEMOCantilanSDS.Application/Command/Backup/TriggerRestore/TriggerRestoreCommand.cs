using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Backup.TriggerRestore;

/// <summary>
/// Head-triggered destructive restore: dispatches the GitHub Actions restore workflow, which takes a
/// safety backup first and then atomically restores the latest backup — REPLACING the production DB.
/// Both the confirmation phrase and the admin password are re-verified SERVER-SIDE in the handler
/// before the workflow is dispatched. The password is never logged.
/// </summary>
public record TriggerRestoreCommand(string ConfirmationPhrase, string Password) : IRequest<Result<bool>>;
