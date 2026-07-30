using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>
/// Two-factor operations that require an AUTHENTICATED caller.
/// <para>
/// Deliberately separate from <see cref="IAuthApiClient"/>: that client is registered without the
/// authorization/refresh delegating handlers (it serves anonymous login, refresh and logout), so any
/// <c>[Authorize]</c> call placed on it would never carry a bearer token and would fail with 401 the moment
/// the access token expired. This client uses the standard authenticated pipeline.
/// </para>
/// </summary>
public interface IMfaApiClient
{
    // ── Own account ──
    Task<Result<MfaStatusDto>> GetMfaStatusAsync();
    Task<Result<MfaEnrollmentDto>> BeginMfaEnrollmentAsync(BeginMfaEnrollmentCommand command);
    Task<Result<MfaRecoveryCodesDto>> ConfirmMfaEnrollmentAsync(ConfirmMfaEnrollmentCommand command);
    Task<Result<bool>> DisableMfaAsync(DisableMfaCommand command);
    Task<Result<MfaRecoveryCodesDto>> RegenerateRecoveryCodesAsync(RegenerateRecoveryCodesCommand command);

    /// <summary>Records that this account has seen the two-factor reminder, so it is offered only once.</summary>
    Task<Result<bool>> AcknowledgeMfaReminderAsync();

    // ── Platform-operator recovery ──
    Task<Result<IReadOnlyList<MfaEnrolledAccountDto>>> GetMfaEnrolledAccountsAsync();
    Task<Result<bool>> ResetUserMfaAsync(ResetUserMfaCommand command);
}
