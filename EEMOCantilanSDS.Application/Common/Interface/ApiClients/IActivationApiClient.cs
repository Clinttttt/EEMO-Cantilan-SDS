using EEMOCantilanSDS.Application.Command.Onboarding.SetAdminPasswordByToken;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IActivationApiClient
{
    /// <summary>
    /// Completes a provisioned Head's activation using their one-time link token: sets the password and
    /// activates the account. Anonymous (the token is the credential) — no logged-in user is required.
    /// </summary>
    Task<Result<bool>> SetPasswordByTokenAsync(SetAdminPasswordByTokenCommand command);
}
