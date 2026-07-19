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

    /// <summary>Re-authentication: verify the current user's own password before a sensitive change
    /// (e.g. changing the online-payment account). True when the password matches.</summary>
    Task<Result<bool>> VerifyMyPasswordAsync(string password);

    /// <summary>The caller Head's current office/LGU branding (to pre-fill the profile edit form).</summary>
    Task<Result<OfficeProfileEditDto>> GetOfficeProfileAsync();

    /// <summary>Update the caller Head's office/LGU branding (office label, acronym, address, seal/logo).</summary>
    Task<Result<bool>> UpdateOfficeProfileAsync(string officeName, string? officeAcronym, string? address, string? sealPath);
}
