using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ISettingsApiClient
{
    Task<Result<SystemSettingsDto>> GetSystemSettingsAsync();

    /// <summary>The caller LGU's online-payment account status (never the secret).</summary>
    Task<Result<PaymentSettingsDto>> GetPaymentSettingsAsync();

    /// <summary>Set (or clear, when secretKey is null/empty) the LGU's own PayMongo credentials.</summary>
    Task<Result<bool>> SavePaymentCredentialsAsync(string? secretKey, string? publicKey, string? webhookSecret);
}
