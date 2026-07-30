using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

/// <summary>
/// Authenticated two-factor client. Registered through the standard <c>AddApiHttpClient</c> pipeline so every
/// call carries the bearer token and benefits from automatic refresh.
/// </summary>
public class MfaApiClient(HttpClient http) : HandleResponse(http), IMfaApiClient
{
    public async Task<Result<MfaStatusDto>> GetMfaStatusAsync() =>
        await GetAsync<MfaStatusDto>("api/AdminAuth/mfa/status");

    public async Task<Result<MfaEnrollmentDto>> BeginMfaEnrollmentAsync(BeginMfaEnrollmentCommand command) =>
        await PostAsync<BeginMfaEnrollmentCommand, MfaEnrollmentDto>("api/AdminAuth/mfa/enroll", command);

    public async Task<Result<MfaRecoveryCodesDto>> ConfirmMfaEnrollmentAsync(ConfirmMfaEnrollmentCommand command) =>
        await PostAsync<ConfirmMfaEnrollmentCommand, MfaRecoveryCodesDto>("api/AdminAuth/mfa/confirm", command);

    public async Task<Result<bool>> DisableMfaAsync(DisableMfaCommand command) =>
        await PostAsync<DisableMfaCommand, bool>("api/AdminAuth/mfa/disable", command);

    public async Task<Result<MfaRecoveryCodesDto>> RegenerateRecoveryCodesAsync(RegenerateRecoveryCodesCommand command) =>
        await PostAsync<RegenerateRecoveryCodesCommand, MfaRecoveryCodesDto>("api/AdminAuth/mfa/recovery-codes", command);

    public async Task<Result<bool>> AcknowledgeMfaReminderAsync() =>
        await PostAsync<object, bool>("api/AdminAuth/mfa/reminder-seen", new { });

    public async Task<Result<IReadOnlyList<MfaEnrolledAccountDto>>> GetMfaEnrolledAccountsAsync() =>
        await GetAsync<IReadOnlyList<MfaEnrolledAccountDto>>("api/AdminAuth/mfa/enrolled-accounts");

    public async Task<Result<bool>> ResetUserMfaAsync(ResetUserMfaCommand command) =>
        await PostAsync<ResetUserMfaCommand, bool>("api/AdminAuth/mfa/reset-user", command);
}
